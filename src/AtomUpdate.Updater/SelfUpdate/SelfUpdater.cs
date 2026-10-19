using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Json;
using AtomUpdate.Updater.Logging;
using AtomUpdate.Updater.Verification;

namespace AtomUpdate.Updater.SelfUpdate;

/// <summary>自升级检查与交接（04 §4.12，Q16）：checkBeforeInstall 失败（断网/不可达）记日志跳过，继续原任务。
/// 清单 URL 为 MVP 内部约定（Q13 独立分发契约二阶段定）；未配置 manifestUrl 时跳过自升级。</summary>
public sealed class SelfUpdater(UpdaterTaskFile task, UpdaterLog log)
{
    public static string CurrentVersion =>
        typeof(SelfUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>有新更新器则下载并交接（返回 true → 本进程退出 0x0000）。</summary>
    public async Task<bool> TryHandoffAsync(CancellationToken ct = default)
    {
        if (!task.SelfUpdate.Enabled || !task.SelfUpdate.CheckBeforeInstall)
        {
            return false;
        }

        var manifestUrl = task.SelfUpdate.ManifestUrl;
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            log.Debug("未配置 selfUpdate.manifestUrl，跳过自升级检查");
            return false;
        }

        SelfUpdateManifest? manifest;
        try
        {
            manifest = await FetchManifestAsync(manifestUrl, ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log.Warning($"自升级检查失败（断网/不可达），跳过升级继续安装：{exception.Message}");
            return false;
        }

        if (manifest?.Version is null || string.IsNullOrWhiteSpace(manifest.Url))
        {
            log.Warning("自升级清单无效（缺少 version/url），跳过");
            return false;
        }

        if (CompareVersions(manifest.Version, CurrentVersion) <= 0)
        {
            log.Debug($"更新器已是最新（当前 {CurrentVersion}，远端 {manifest.Version}）");
            return false;
        }

        log.Information($"发现新更新器：{CurrentVersion} → {manifest.Version}");

        var directory = Path.Combine(Path.GetTempPath(), "atomupdate", "selfupdate");
        Directory.CreateDirectory(directory);
        var newPath = Path.Combine(directory, "AtomUpdate.Updater.New" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        try
        {
            await DownloadAsync(manifest.Url, newPath, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                var actual = PackageVerifier.ComputeSha256(newPath);
                if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(newPath);
                    log.Error($"新更新器 SHA256 校验失败（期望 {manifest.Sha256}，实际 {actual}），跳过自升级");
                    return false;
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(newPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception exception)
        {
            log.Warning($"新更新器下载失败，跳过自升级继续安装：{exception.Message}");
            return false;
        }

        try
        {
            var handoff = Path.Combine(directory, $"handoff-{Guid.NewGuid():N}.json");
            File.WriteAllText(handoff, JsonSerializer.Serialize(task, UpdaterJsonContext.Default.UpdaterTaskFile));

            var current = Environment.ProcessPath!;
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = newPath,
                Arguments = $"--replace-old \"{current}\" --replace-pid {Environment.ProcessId} --config \"{handoff}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                log.Warning("新更新器启动失败，跳过自升级继续安装");
                return false;
            }

            log.Information($"已交接给新更新器（PID {process.Id}），本进程退出等待替换");
            return true;
        }
        catch (Exception exception)
        {
            log.Warning($"自升级交接失败，继续使用当前更新器：{exception.Message}");
            return false;
        }
    }

    private async Task<SelfUpdateManifest?> FetchManifestAsync(string url, CancellationToken ct)
    {
        using var client = CreateClient();
        var json = await client.GetStringAsync(url, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, UpdaterJsonContext.Default.SelfUpdateManifest);
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        using var client = CreateClient();
        var attempts = Math.Max(1, task.Network.RetryCount + 1);
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var source = await client.GetStreamAsync(url, ct).ConfigureAwait(false);
                await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (attempt < attempts)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
            }
        }
    }

    private HttpClient CreateClient()
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(task.Network.Proxy))
        {
            try
            {
                handler.Proxy = new System.Net.WebProxy(task.Network.Proxy);
                handler.UseProxy = true;
            }
            catch (Exception)
            {
                // 代理配置无效时按系统默认
            }
        }

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(1, task.Network.TimeoutSeconds)) };
    }

    /// <summary>语义化版本比较（忽略 +build 与 -prerelease 后缀差异，MVP 口径）。</summary>
    public static int CompareVersions(string left, string right)
    {
        var a = ParseVersion(left);
        var b = ParseVersion(right);
        return a.CompareTo(b);
    }

    private static Version ParseVersion(string value)
    {
        var cleaned = value.Split('+')[0].Split('-')[0];
        return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0);
    }
}
