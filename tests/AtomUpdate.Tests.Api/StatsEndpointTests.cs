using System.Net;
using System.Text.Json;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Api.Endpoints.Reports;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Tests.Api;

/// <summary>manage 统计读端点（M6）：安装汇总（指纹去重）/ 下载汇总 / 检查汇总（304·策略命中）/ 分页与关键字 / 租户隔离。
/// 数据经真实上报端点与 /api/check 种子化，异步日志等待落库后再断言。</summary>
[Collection("api")]
public class StatsEndpointTests(ApiFixture fixture)
{
    private string StatsPath(string path) => $"{ApiRoutes.ManagePrefix}/stats/{path}";

    /// <summary>种子：3 安装（a/b 同指纹 fp-1、版本 2.0.0；c 无指纹 1.0.0；a 通道 stable）+ 2 下载（delta 成功 / full 失败）+ 3 检查（普通 / 304 / 策略未变）。</summary>
    private async Task<Scenario> SeedAsync()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var instA = $"inst-a-{Guid.NewGuid():N}";
        var instB = $"inst-b-{Guid.NewGuid():N}";
        var instC = $"inst-c-{Guid.NewGuid():N}";

        (await fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsInstall, new StatsInstallRequest
        {
            ProjectKey = scenario.ProjectKey,
            AppId = scenario.AppBusinessId,
            InstallationId = instA,
            Version = "1.0.0",
            Channel = UpdateChannel.Stable
        })).Expect(HttpStatusCode.Accepted);

        JsonElement Startup(string installationId, string? fingerprint, string version) =>
            JsonSerializer.SerializeToElement(new
            {
                type = "startup",
                @event = new StartupReport
                {
                    ProjectKey = scenario.ProjectKey,
                    AppId = scenario.AppBusinessId,
                    InstallationId = installationId,
                    MachineFingerprint = fingerprint,
                    Version = version
                }
            }, ApiFixture.Json);

        (await fixture.SendJsonAsync<StatsBatchResponse>(HttpMethod.Post, ApiRoutes.StatsBatch,
                new List<JsonElement>
                {
                    Startup(instA, "fp-1", "2.0.0"),
                    Startup(instB, "fp-1", "2.0.0"),
                    Startup(instC, null, "1.0.0")
                }))
            .Expect(HttpStatusCode.OK);

        Task<ApiResult<object>> PostDownload(string installationId, AssetKind kind, bool success, long bytes,
            long durationMs, string? errorCode = null) =>
            fixture.SendJsonAsync<object>(HttpMethod.Post, ApiRoutes.StatsDownload, new DownloadReport
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
            });

        (await PostDownload(instA, AssetKind.Delta, success: true, bytes: 100, durationMs: 40))
            .Expect(HttpStatusCode.Accepted);
        (await PostDownload(instB, AssetKind.Full, success: false, bytes: 60, durationMs: 80, errorCode: "NET_ERR"))
            .Expect(HttpStatusCode.Accepted);

        // 3 次检查：普通 200 → ETag 重验证 304 → 策略版本匹配（policyUnchanged）
        var first = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"), ifNoneMatch: first.ETag))
            .Expect(HttpStatusCode.NotModified);
        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", policyVersion: 1)))
            .Expect(HttpStatusCode.OK);

        // 异步日志（下载 / 检查）等待落库
        await fixture.WaitForDbAsync(scenario.TenantId, async db => await db.DownloadLogs.AsNoTracking()
            .CountAsync(l => l.AppId == scenario.AppId) >= 2);
        await fixture.WaitForDbAsync(scenario.TenantId, async db => await db.UpdateCheckLogs.AsNoTracking()
            .CountAsync(l => l.AppId == scenario.AppId) >= 3);

        return scenario;
    }

    [Fact]
    public async Task InstallationSummary_FingerprintDedup_VersionAndChannelDistribution()
    {
        var scenario = await SeedAsync();

        var summary = (await fixture.SendJsonAsync<InstallationSummaryDto>(HttpMethod.Get,
                StatsPath($"installations/summary?appId={scenario.AppId}"), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;

        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.DistinctFingerprints); // fp-1 两台机器去重
        Assert.Equal(1, summary.WithoutFingerprint);
        Assert.Equal(3, summary.Active24h);
        Assert.Equal(3, summary.Active7d);

        var top = Assert.Single(summary.ByVersion, v => v.Count == 2);
        Assert.Equal("2.0.0", top.Version);
        Assert.Contains(summary.ByVersion, v => v.Version == "1.0.0" && v.Count == 1);

        var channel = Assert.Single(summary.ByChannel);
        Assert.Equal(UpdateChannel.Stable, channel.Channel);
        Assert.Equal(1, channel.Count);
    }

    [Fact]
    public async Task DownloadSummary_SuccessRate_Bytes_DeltaShare()
    {
        var scenario = await SeedAsync();

        var summary = (await fixture.SendJsonAsync<DownloadSummaryDto>(HttpMethod.Get,
                StatsPath($"downloads/summary?appId={scenario.AppId}"), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;

        Assert.Equal(2, summary.Downloads24h);
        Assert.Equal(2, summary.Downloads7d);
        Assert.Equal(0.5, summary.SuccessRate7d, 3);
        Assert.Equal(160, summary.Bytes7d);
        Assert.Equal(60, summary.AvgDurationMs7d, 3);
        Assert.Equal(0.5, summary.DeltaShare7d, 3);
    }

    [Fact]
    public async Task CheckSummary_ETagHitRate_And_PolicyUnchangedRate()
    {
        var scenario = await SeedAsync();

        var summary = (await fixture.SendJsonAsync<CheckSummaryDto>(HttpMethod.Get,
                StatsPath($"checks/summary?appId={scenario.AppId}"), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;

        Assert.Equal(3, summary.Checks24h);
        Assert.Equal(3, summary.Checks7d);
        Assert.Equal(1.0 / 3, summary.ETagHitRate7d, 3);
        Assert.Equal(1.0 / 3, summary.PolicyUnchangedRate7d, 3);
    }

    [Fact]
    public async Task Installations_Paged_WithKeywordFilter()
    {
        var scenario = await SeedAsync();

        var page1 = (await fixture.SendJsonAsync<PagedResult<InstallationDto>>(HttpMethod.Get,
                StatsPath($"installations?appId={scenario.AppId}&page=1&pageSize=2"), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.Items.Count);

        var page2 = (await fixture.SendJsonAsync<PagedResult<InstallationDto>>(HttpMethod.Get,
                StatsPath($"installations?appId={scenario.AppId}&page=2&pageSize=2"), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(3, page2.Total);
        Assert.Single(page2.Items);
        Assert.NotEqual(page1.Items[0].InstallationId, page2.Items[0].InstallationId);

        var filtered = (await fixture.SendJsonAsync<PagedResult<InstallationDto>>(HttpMethod.Get,
                StatsPath($"installations?appId={scenario.AppId}&keyword=inst-b-"), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        var match = Assert.Single(filtered.Items);
        Assert.StartsWith("inst-b-", match.InstallationId);
        Assert.Equal("fp-1", match.MachineFingerprintHash);
        Assert.Equal("2.0.0", match.LastVersion);
    }

    [Fact]
    public async Task Stats_AreIsolatedPerTenant()
    {
        var seeded = await SeedAsync();
        var other = await fixture.CreateScenarioAsync();

        // 其他租户无任何上报：整体汇总为空
        var empty = (await fixture.SendJsonAsync<InstallationSummaryDto>(HttpMethod.Get,
                StatsPath("installations/summary"), bearer: other.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(0, empty.Total);

        // 跨租户按 appId 过滤同样不可见（全局过滤器在 appId 谓词之前生效）
        var crossTenant = (await fixture.SendJsonAsync<InstallationSummaryDto>(HttpMethod.Get,
                StatsPath($"installations/summary?appId={seeded.AppId}"), bearer: other.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(0, crossTenant.Total);

        var checks = (await fixture.SendJsonAsync<CheckSummaryDto>(HttpMethod.Get,
                StatsPath("checks/summary"), bearer: other.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(0, checks.Checks7d);
    }
}
