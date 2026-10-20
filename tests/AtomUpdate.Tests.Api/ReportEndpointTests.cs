using System.Net;
using System.Text.Json;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Api.Endpoints.Reports;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Tests.Api;

/// <summary>上报端点：公开匿名上报（按请求体反查租户）/ install 幂等 / batch 分派与坏行跳过 / 不存在应用不泄露 / Installation upsert。</summary>
[Collection("api")]
public class ReportEndpointTests(ApiFixture fixture)
{
    private static StatsInstallRequest InstallBody(Scenario scenario, string installationId,
        string? version = null, UpdateChannel? channel = null) => new()
    {
        ProjectKey = scenario.ProjectKey,
        AppId = scenario.AppBusinessId,
        InstallationId = installationId,
        Version = version,
        Channel = channel
    };

    private static InstallReport InstallReportBody(Scenario scenario, string installationId,
        string toVersion = "2.0.0", string? fingerprint = null, string? errorCode = null) => new()
    {
        ProjectKey = scenario.ProjectKey,
        AppId = scenario.AppBusinessId,
        InstallationId = installationId,
        MachineFingerprint = fingerprint,
        FromVersion = "1.0.0",
        ToVersion = toVersion,
        Stage = UpdateStage.Completed,
        InstallMode = InstallMode.Immediate,
        Elevated = true,
        ErrorCode = errorCode
    };

    private static DownloadReport DownloadBody(Scenario scenario, string installationId,
        AssetKind? kind = AssetKind.Delta, bool success = true, long bytes = 100, long durationMs = 40,
        string? errorCode = null) => new()
    {
        ProjectKey = scenario.ProjectKey,
        AppId = scenario.AppBusinessId,
        InstallationId = installationId,
        FromVersion = "1.0.0",
        ToVersion = "2.0.0",
        Kind = kind,
        Success = success,
        Bytes = bytes,
        DurationMs = durationMs,
        ErrorCode = errorCode
    };

    private static JsonElement Envelope(string type, object ev) =>
        JsonSerializer.SerializeToElement(new { type, @event = ev }, ApiFixture.Json);

    // ---------- 鉴权 ----------

    [Fact]
    public async Task Anonymous_Report_IsAccepted()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var installationId = Guid.NewGuid().ToString("D");

        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsInstall,
                InstallBody(scenario, installationId, "1.0.0")))
            .Expect(HttpStatusCode.Accepted);

        // 匿名路径按 projectKey/appId 反查租户后正常落库
        var row = await fixture.QueryAsync(scenario.TenantId, db => db.Installations.AsNoTracking()
            .SingleOrDefaultAsync(i => i.InstallationId == installationId));
        Assert.NotNull(row);
        Assert.Equal("1.0.0", row.LastVersion);
    }

    [Fact]
    public async Task JwtSession_CanReport()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var installationId = Guid.NewGuid().ToString("D");

        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsInstall,
                InstallBody(scenario, installationId, "1.0.0"), bearer: fixture.MintToken(scenario.TenantId)))
            .Expect(HttpStatusCode.Accepted);

        var row = await fixture.QueryAsync(scenario.TenantId, db => db.Installations.AsNoTracking()
            .SingleOrDefaultAsync(i => i.InstallationId == installationId));
        Assert.NotNull(row);
        Assert.Equal("1.0.0", row.LastVersion);
    }

    // ---------- /api/stats/install：upsert ----------

    [Fact]
    public async Task StatsInstall_Upserts_ByAppAndInstallationId()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var installationId = Guid.NewGuid().ToString("D");

        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsInstall,
                InstallBody(scenario, installationId, "1.0.0", UpdateChannel.Stable)))
            .Expect(HttpStatusCode.Accepted);
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsInstall,
                InstallBody(scenario, installationId, "2.0.0")))
            .Expect(HttpStatusCode.Accepted);

        var rows = await fixture.QueryAsync(scenario.TenantId, db => db.Installations.AsNoTracking()
            .Where(i => i.InstallationId == installationId).ToListAsync());

        // 同键二次上报刷新而非新增：单行，版本/活跃时间被更新；未提供的字段（Channel）保留原值
        var row = Assert.Single(rows);
        Assert.Equal("2.0.0", row.LastVersion);
        Assert.Equal(UpdateChannel.Stable, row.Channel);
        Assert.Equal(scenario.TenantId, row.TenantId);
        Assert.Equal(scenario.AppId, row.AppId);
        Assert.True(row.LastSeenAt >= row.FirstSeenAt);
    }

    [Fact]
    public async Task StatsInstall_OverlongInstallationId_Dropped()
    {
        var scenario = await fixture.CreateScenarioAsync();
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsInstall,
                InstallBody(scenario, new string('x', 65))))
            .Expect(HttpStatusCode.Accepted);

        var count = await fixture.QueryAsync(scenario.TenantId, db => db.Installations
            .CountAsync(i => i.AppId == scenario.AppId));
        Assert.Equal(0, count);
    }

    // ---------- /api/report/install：幂等 ----------

    [Fact]
    public async Task InstallReport_IsIdempotent_DuplicateReturns200_SingleRow()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var installationId = Guid.NewGuid().ToString("D");
        var body = InstallReportBody(scenario, installationId, fingerprint: "fp-dup");

        var first = (await fixture.SendJsonAsync<ReportInstallResponse>(HttpMethod.Post, ApiRoutes.ReportInstall,
                body))
            .Expect(HttpStatusCode.OK).Data;
        Assert.True(first.Accepted);
        Assert.False(first.Duplicate);

        // 更新器重试/离线补报直传同一报告：唯一索引冲突视为已上报，不放大行数
        var second = (await fixture.SendJsonAsync<ReportInstallResponse>(HttpMethod.Post, ApiRoutes.ReportInstall,
                body))
            .Expect(HttpStatusCode.OK).Data;
        Assert.True(second.Accepted);
        Assert.True(second.Duplicate);

        var rows = await fixture.QueryAsync(scenario.TenantId, db => db.InstallationReports.AsNoTracking()
            .Where(r => r.InstallationId == installationId).ToListAsync());
        var report = Assert.Single(rows);
        Assert.Equal("1.0.0", report.FromVersion);
        Assert.Equal("2.0.0", report.ToVersion);
        Assert.Equal(UpdateStage.Completed, report.Stage);
        Assert.Equal(InstallMode.Immediate, report.InstallMode);
        Assert.True(report.Elevated);

        // 联动 upsert：指纹写入 Installation
        var installation = await fixture.QueryAsync(scenario.TenantId, db => db.Installations.AsNoTracking()
            .SingleAsync(i => i.InstallationId == installationId));
        Assert.Equal("fp-dup", installation.MachineFingerprintHash);
        Assert.Equal("2.0.0", installation.LastVersion);
    }

    // ---------- /api/stats/download：异步落库 ----------

    [Fact]
    public async Task StatsDownload_EnqueuesDownloadLog_AndUpsertsInstallation()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var installationId = Guid.NewGuid().ToString("D");

        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsDownload,
                DownloadBody(scenario, installationId, kind: AssetKind.Delta, bytes: 12345, durationMs: 67)))
            .Expect(HttpStatusCode.Accepted);

        // DownloadLog 走后台队列批量落库，需等待写入器刷新
        await fixture.WaitForDbAsync(scenario.TenantId, db => db.DownloadLogs.AsNoTracking()
            .AnyAsync(l => l.InstallationId == installationId));

        var log = await fixture.QueryAsync(scenario.TenantId, db => db.DownloadLogs.AsNoTracking()
            .SingleAsync(l => l.InstallationId == installationId));
        Assert.Equal("1.0.0", log.FromVersion);
        Assert.Equal("2.0.0", log.ToVersion);
        Assert.Equal(AssetKind.Delta, log.Kind);
        Assert.True(log.Success);
        Assert.Equal(12345, log.Bytes);
        Assert.Equal(67, log.DurationMs);
        Assert.Equal(scenario.TenantId, log.TenantId);

        // Installation 同步 upsert（无需等待）
        var installation = await fixture.QueryAsync(scenario.TenantId, db => db.Installations.AsNoTracking()
            .SingleAsync(i => i.InstallationId == installationId));
        Assert.Equal("2.0.0", installation.LastVersion);
    }

    // ---------- /api/stats/batch：分派与跳过 ----------

    [Fact]
    public async Task Batch_DispatchesKnownTypes_SkipsCheckAndBadRows()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var downloadInst = $"dl-{Guid.NewGuid():N}";
        var startupInst = $"st-{Guid.NewGuid():N}";
        var installInst = $"in-{Guid.NewGuid():N}";

        var batch = new List<JsonElement>
        {
            Envelope("download", DownloadBody(scenario, downloadInst, kind: AssetKind.Full, success: false,
                bytes: 0, durationMs: 5, errorCode: "NET_ERR")),
            Envelope("startup", new StartupReport
            {
                ProjectKey = scenario.ProjectKey,
                AppId = scenario.AppBusinessId,
                InstallationId = startupInst,
                MachineFingerprint = "fp-batch",
                Version = "2.0.0"
            }),
            Envelope("install", InstallReportBody(scenario, installInst)),
            // check 事件已由 /api/check 服务端落 UpdateCheckLog，batch 不双计
            Envelope("check", new CheckReport
            {
                ProjectKey = scenario.ProjectKey,
                AppId = scenario.AppBusinessId,
                InstallationId = Guid.NewGuid().ToString("D"),
                CurrentVersion = "1.0.0",
                HasUpdate = true
            }),
            JsonSerializer.SerializeToElement(new { foo = 1 }, ApiFixture.Json), // 缺 type/event 信封
            Envelope("wat", new { }) // 未知类型
        };

        var result = (await fixture.SendJsonAsync<StatsBatchResponse>(HttpMethod.Post, ApiRoutes.StatsBatch,
                batch))
            .Expect(HttpStatusCode.OK).Data;

        Assert.Equal(3, result.Accepted);
        Assert.Equal(3, result.Skipped);

        await fixture.WaitForDbAsync(scenario.TenantId, db => db.DownloadLogs.AsNoTracking()
            .AnyAsync(l => l.InstallationId == downloadInst));

        var download = await fixture.QueryAsync(scenario.TenantId, db => db.DownloadLogs.AsNoTracking()
            .SingleAsync(l => l.InstallationId == downloadInst));
        Assert.Equal(AssetKind.Full, download.Kind);
        Assert.False(download.Success);
        Assert.Equal("NET_ERR", download.ErrorCode);

        var report = await fixture.QueryAsync(scenario.TenantId, db => db.InstallationReports.AsNoTracking()
            .SingleAsync(r => r.InstallationId == installInst));
        Assert.Equal("2.0.0", report.ToVersion);

        // startup 刷新 Installation 指纹与版本
        var startup = await fixture.QueryAsync(scenario.TenantId, db => db.Installations.AsNoTracking()
            .SingleAsync(i => i.InstallationId == startupInst));
        Assert.Equal("fp-batch", startup.MachineFingerprintHash);
        Assert.Equal("2.0.0", startup.LastVersion);

        var installationCount = await fixture.QueryAsync(scenario.TenantId, db => db.Installations
            .CountAsync(i => i.AppId == scenario.AppId));
        Assert.Equal(3, installationCount);
    }

    // ---------- 不泄露应用存在性 ----------

    [Fact]
    public async Task UnknownApp_DroppedSilently()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var installationId = Guid.NewGuid().ToString("D");
        const string bogusApp = "com.missing.app";

        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsInstall,
                new StatsInstallRequest
                {
                    ProjectKey = scenario.ProjectKey, AppId = bogusApp, InstallationId = installationId
                }))
            .Expect(HttpStatusCode.Accepted);
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsDownload,
                new DownloadReport
                {
                    ProjectKey = scenario.ProjectKey,
                    AppId = bogusApp,
                    InstallationId = installationId,
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0"
                }))
            .Expect(HttpStatusCode.Accepted);

        var installResult = (await fixture.SendJsonAsync<ReportInstallResponse>(HttpMethod.Post,
                ApiRoutes.ReportInstall,
                new InstallReport
                {
                    ProjectKey = scenario.ProjectKey,
                    AppId = bogusApp,
                    InstallationId = installationId,
                    ToVersion = "2.0.0"
                }))
            .Expect(HttpStatusCode.OK).Data;
        Assert.False(installResult.Accepted);

        // 同步路径断言无任何落库（download 未解析到应用时根本不入队）
        var installations = await fixture.QueryAsync(scenario.TenantId, db => db.Installations
            .CountAsync(i => i.InstallationId == installationId));
        var reports = await fixture.QueryAsync(scenario.TenantId, db => db.InstallationReports
            .CountAsync(r => r.InstallationId == installationId));
        Assert.Equal(0, installations);
        Assert.Equal(0, reports);
    }
}
