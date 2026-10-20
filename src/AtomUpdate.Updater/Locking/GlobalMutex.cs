using System.Diagnostics;
using System.Text;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Locking;

/// <summary>全局互斥（04 §4.11）：Windows 命名互斥体 Global\AtomUpdate.Updater.{appId}；
/// macOS/Linux /tmp/atomupdate.updater.{appId}.lock 文件锁 + PID 校验（死进程残留锁可接管）。
/// 获取失败 → 0x1001（不提示用户，只写日志）。</summary>
public sealed class GlobalMutex : IDisposable
{
    private readonly System.Threading.Mutex? _mutex;
    private readonly FileStream? _lockFile;
    private readonly string? _lockPath;

    public string Name { get; }

    private GlobalMutex(System.Threading.Mutex? mutex, FileStream? lockFile, string? lockPath, string name)
    {
        _mutex = mutex;
        _lockFile = lockFile;
        _lockPath = lockPath;
        Name = name;
    }

    public static GlobalMutex? TryAcquire(string appId, int timeoutSeconds, UpdaterLog? log, out string failureReason)
    {
        failureReason = "";
        var safe = UpdaterPaths.Sanitize(appId).Replace('\\', '_');
        var timeout = TimeSpan.FromSeconds(Math.Max(0, timeoutSeconds));

        if (OperatingSystem.IsWindows())
        {
            var name = $@"Global\AtomUpdate.Updater.{safe}";
            System.Threading.Mutex mutex;
            try
            {
                mutex = new System.Threading.Mutex(false, name);
            }
            catch (UnauthorizedAccessException)
            {
                name = $@"Local\AtomUpdate.Updater.{safe}";
                mutex = new System.Threading.Mutex(false, name);
                log?.Warning($"Global 互斥体不可用，回退本地命名：{name}");
            }

            bool acquired;
            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // 前任进程异常退出未释放：按获取成功处理
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                failureReason = $"互斥体被占用：{name}";
                return null;
            }

            return new GlobalMutex(mutex, null, null, name);
        }

        var path = Path.Combine(Path.GetTempPath(), $"atomupdate.updater.{safe}.lock");
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (TryOpenLockFile(path, out var stream, out var error, log))
            {
                return new GlobalMutex(null, stream, path, path);
            }

            if (error == LockError.Stale)
            {
                continue;
            }

            if (DateTime.UtcNow >= deadline)
            {
                failureReason = $"文件锁被占用：{path}";
                return null;
            }

            Thread.Sleep(100);
        }
    }

    private enum LockError
    {
        None,
        Busy,
        Stale,
        NoAccess
    }

    private static bool TryOpenLockFile(string path, out FileStream? stream, out LockError error, UpdaterLog? log)
    {
        stream = null;
        error = LockError.None;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            stream.Write(Encoding.ASCII.GetBytes(Environment.ProcessId.ToString()));
            stream.Flush();
            return true;
        }
        catch (IOException)
        {
            // 被占用；校验 PID 是否为死进程残留
            if (TryClearStale(path, log))
            {
                error = LockError.Stale;
                return false;
            }

            error = LockError.Busy;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = LockError.NoAccess;
            return false;
        }
    }

    private static bool TryClearStale(string path, UpdaterLog? log)
    {
        try
        {
            var text = File.ReadAllText(path).Trim();
            if (!int.TryParse(text, out var pid) || !IsProcessAlive(pid))
            {
                File.Delete(path);
                log?.Warning($"清理残留锁文件（PID 已不存在）：{path}");
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            // 读不到（正被持有）或删除失败：按仍占用处理
            return false;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (Exception)
            {
                // 非持有线程/已释放等场景忽略
            }

            _mutex.Dispose();
        }

        if (_lockFile is not null)
        {
            _lockFile.Dispose();
            try
            {
                if (_lockPath is not null && File.Exists(_lockPath))
                {
                    File.Delete(_lockPath);
                }
            }
            catch (Exception)
            {
                // 残留锁由 PID 校验接管
            }
        }
    }
}
