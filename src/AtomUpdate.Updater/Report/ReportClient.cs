using System.Net.Http.Json;
using System.Text.Json;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Json;
using AtomUpdate.Updater.Logging;

namespace AtomUpdate.Updater.Report;

/// <summary>回传（04 §5.3）：POST /api/report/install，重试 3 次指数退避、超时 15s；失败保持状态 reported=false 等 SDK 补报。
/// 更新器不假设网络可用——回传失败不影响安装结果。</summary>
public sealed class ReportClient(UpdaterTaskFile task, UpdaterLog log)
{
    public async Task<bool> SendAsync(InstallReport report, CancellationToken ct = default)
    {
        if (!task.Report.Enabled)
        {
            log.Debug("report.enabled=false，跳过回传");
            return false;
        }

        var serverUrl = task.Report.ServerUrl ?? task.ServerUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            log.Warning("未配置 serverUrl，无法回传（状态保留 reported=false 由 SDK 补报）");
            return false;
        }

        var url = serverUrl.TrimEnd('/') + "/api/report/install";
        var attempts = Math.Max(1, task.Report.RetryCount + 1);
        var delay = TimeSpan.FromSeconds(1);

        using var handler = CreateHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, task.Report.TimeoutSeconds))
        };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await client
                    .PostAsJsonAsync(url, report, UpdaterJsonContext.Default.InstallReport, ct)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    log.Information($"安装回传成功（HTTP {(int)response.StatusCode}）");
                    return true;
                }

                log.Warning($"安装回传被拒：HTTP {(int)response.StatusCode}");
            }
            catch (Exception exception)
            {
                if (ct.IsCancellationRequested && exception is OperationCanceledException)
                {
                    return false;
                }

                log.Warning($"安装回传失败（第 {attempt} 次）：{exception.Message}");
            }

            if (attempt >= attempts)
            {
                log.Warning("安装回传未送达，状态保留 reported=false 等 SDK 补报");
                return false;
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
        }
    }

    private HttpClientHandler CreateHandler()
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(task.Network.Proxy))
        {
            try
            {
                handler.Proxy = new System.Net.WebProxy(task.Network.Proxy);
                handler.UseProxy = true;
            }
            catch (Exception exception)
            {
                log.Warning($"代理配置无效：{exception.Message}");
            }
        }

        return handler;
    }
}
