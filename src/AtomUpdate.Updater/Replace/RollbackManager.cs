using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Replace;

/// <summary>回滚（04 §4.14）：从备份恢复被覆盖/删除的文件 → 删除本次新建的文件。全部成功才算回滚成功（0x1009 提示手动恢复）。</summary>
public static class RollbackManager
{
    public static bool Restore(string targetRoot, BackupManifest manifest, int retryCount, int retryDelayMs,
        UpdaterLog log)
    {
        var success = true;

        foreach (var relative in manifest.BackedUpFiles)
        {
            if (manifest.BackupDirectory is null)
            {
                return false;
            }

            var source = Path.Combine(manifest.BackupDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!TryWithRetry(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, overwrite: true);
                }, retryCount, retryDelayMs, log, $"恢复 {relative}"))
            {
                success = false;
            }
        }

        foreach (var relative in manifest.CreatedFiles)
        {
            var destination = Path.Combine(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(destination))
            {
                continue;
            }

            if (!TryWithRetry(() => File.Delete(destination), retryCount, retryDelayMs, log, $"删除新建文件 {relative}"))
            {
                success = false;
            }
        }

        return success;
    }

    private static bool TryWithRetry(Action operation, int retryCount, int retryDelayMs, UpdaterLog log, string what)
    {
        var attempts = Math.Max(1, retryCount + 1);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                operation();
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts)
                {
                    log.Error($"回滚操作失败：{what}（{exception.Message}）");
                    return false;
                }

                log.Debug($"{what} 重试 {attempt}/{attempts}：{exception.Message}");
                Thread.Sleep(Math.Max(0, retryDelayMs));
            }
            catch (Exception exception)
            {
                log.Error($"回滚操作异常：{what}（{exception.Message}）");
                return false;
            }
        }
    }
}
