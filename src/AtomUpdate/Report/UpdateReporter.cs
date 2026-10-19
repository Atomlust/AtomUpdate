using System.Net.Http.Json;
using System.Text.Json;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Check;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Serialization;
using AtomUpdate.Internal;

namespace AtomUpdate.Report;

/// <summary>状态上报（04 §3.3/§3.14）：失败落盘 pending-reports，启动补报（Q15 离线兜底）。批量端点归 M6（当前服务端未实现时自动走离线队列）。stateRootOverride 供测试隔离，与 FileStateStore 一致。</summary>
public sealed class UpdateReporter(
    IHttpClientFactory httpClientFactory,
    AtomUpdateOptions options,
    IUpdateLogger logger,
    string? stateRootOverride = null) : IUpdateReporter
{
    private static readonly JsonSerializerOptions Json = AtomUpdateJson.Create();

    public async Task<bool> ReportCheckAsync(CheckReport report, CancellationToken ct = default) =>
        await SendBatchAsync([Wrap("check", report)], ct);

    public async Task<bool> ReportDownloadAsync(DownloadReport report, CancellationToken ct = default) =>
        await SendBatchAsync([Wrap("download", report)], ct);

    public async Task<bool> ReportInstallAsync(InstallReport? report, CancellationToken ct = default)
    {
        if (report is not null && !await SendInstallAsync(report, ct))
        {
            await PersistAsync(Wrap("install", report), ct);
            return false;
        }

        return true;
    }

    public async Task<bool> ReportStartupAsync(StartupReport report, CancellationToken ct = default)
    {
        var sent = await SendBatchAsync([Wrap("startup", report)], ct);

        // 冲刷补报队列：成功一条删一条
        if (options.Cache.EnableOfflineReport && options.Report.Enabled)
        {
            var directory = PendingDirectory();
            if (Directory.Exists(directory))
            {
                var pending = Directory.EnumerateFiles(directory).Order().ToArray();
                if (pending.Length > 0)
                {
                    var envelopes = new List<object>(pending.Length);
                    foreach (var file in pending)
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(file, ct);
                            envelopes.Add(JsonSerializer.Deserialize<JsonElement>(json, Json));
                        }
                        catch (Exception exception)
                        {
                            logger.Warning($"补报队列文件损坏，丢弃：{Path.GetFileName(file)}", exception);
                            File.Delete(file);
                        }
                    }

                    if (envelopes.Count > 0 && await SendBatchAsync(envelopes, ct))
                    {
                        foreach (var file in pending.Where(File.Exists))
                        {
                            File.Delete(file);
                        }

                        logger.Information($"补报完成（{envelopes.Count} 条）");
                    }
                }
            }
        }

        return sent;
    }

    public async Task<bool> ReportBatchAsync(IReadOnlyList<object> events, CancellationToken ct = default) =>
        await SendBatchAsync(events, ct);

    private static object Wrap(string type, object @event) => new { type, @event };

    private async Task<bool> SendBatchAsync(IReadOnlyList<object> events, CancellationToken ct)
    {
        if (!options.Report.Enabled || events.Count == 0)
        {
            return true;
        }

        var client = httpClientFactory.CreateClient(AtomUpdateHttp.ReportClientName);
        var delivered = false;
        try
        {
            using var response = await client.PostAsJsonAsync(ApiRoutes.StatsBatch, events, Json, ct);
            delivered = response.IsSuccessStatusCode;
            if (!delivered)
            {
                // 非 2xx 同样是失败（例如批量端点未上线返回 404），要落盘等补报
                logger.Warning($"批量上报被拒：HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception exception)
        {
            logger.Warning("批量上报失败", exception);
        }

        if (!delivered && options.Report.FallbackToLocal && options.Cache.EnableOfflineReport)
        {
            logger.Warning("已落盘待补报");
            await PersistAsync(events, ct);
        }

        return delivered;
    }

    private async Task<bool> SendInstallAsync(InstallReport report, CancellationToken ct)
    {
        if (!options.Report.Enabled)
        {
            return true;
        }

        var client = httpClientFactory.CreateClient(AtomUpdateHttp.ReportClientName);
        try
        {
            using var response = await client.PostAsJsonAsync(ApiRoutes.ReportInstall, report, Json, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            logger.Warning("安装上报失败，已落盘待补报", exception);
            return false;
        }
    }

    private async Task PersistAsync(object payload, CancellationToken ct)
    {
        var directory = PendingDirectory();
        var file = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(payload, Json), ct);
    }

    private async Task PersistAsync(IReadOnlyList<object> events, CancellationToken ct)
    {
        foreach (var @event in events)
        {
            await PersistAsync(@event, ct);
        }
    }

    private string PendingDirectory()
    {
        var directory = stateRootOverride is null
            ? PlatformPaths.AppScoped("state", options.AppId, "pending-reports")
            : Path.Combine(stateRootOverride, PlatformPaths.Sanitize(options.AppId), "pending-reports");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
