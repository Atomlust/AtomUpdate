using System.Net;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Api.Telemetry;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Core.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtomUpdate.Tests.Api;

/// <summary>检测记录（更新检查逐条快照）：列表字段口径（时间戳/IP/当前版本/应用标识）、筛选与每应用 500 条上限。</summary>
[Collection("api")]
public class CheckLogTests(ApiFixture fixture)
{
    private const string ChecksPath = $"{ApiRoutes.ManagePrefix}/stats/checks";

    [Fact]
    public async Task List_ReturnsSnapshotFields_And_SupportsFilters()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var tenantId = scenario.TenantId;

        await fixture.QueryAsync(tenantId, db =>
        {
            db.UpdateCheckLogs.AddRange(
                MakeLog(scenario, installation: "inst-a", version: "1.0.0", ip: "203.0.113.7"),
                MakeLog(scenario, installation: "inst-b", version: "1.1.0", ip: "203.0.113.8"),
                MakeLog(scenario, installation: "inst-c", version: "2.0.0", ip: null));
            return Task.FromResult(db.SaveChanges());
        });

        var list = (await fixture.SendJsonAsync<PagedResult<CheckLogDto>>(HttpMethod.Get,
            $"{ChecksPath}?appId={scenario.AppId}&page=1&pageSize=20", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.True(list.Data.Total >= 3);
        var newest = list.Data.Items[0];

        // 快照字段：时间戳、IP、当前版本、应用标识（AppName 冗余展示）；最新一条为 inst-c（无 IP）
        Assert.Equal("2.0.0", newest.CurrentVersion);
        Assert.Equal(scenario.AppBusinessId, newest.AppKey);
        Assert.False(string.IsNullOrWhiteSpace(newest.AppName));
        Assert.Null(newest.ClientIp);
        Assert.True(newest.CheckedAt > DateTimeOffset.UtcNow.AddMinutes(-5));

        // keyword：按 IP 模糊匹配
        var byIp = (await fixture.SendJsonAsync<PagedResult<CheckLogDto>>(HttpMethod.Get,
            $"{ChecksPath}?appId={scenario.AppId}&keyword=203.0.113.7", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(1, byIp.Data.Total);
        Assert.Equal("1.0.0", byIp.Data.Items[0].CurrentVersion);
    }

    [Fact]
    public async Task Trim_Keeps_Latest500_PerApp()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var tenantId = scenario.TenantId;

        // 520 条：版本号 1.0.1~1.0.520 递增（Id 同序递增），清理后应保留最近 500 条（1.0.21~1.0.520）
        var logs = Enumerable.Range(1, 520)
            .Select(i => MakeLog(scenario, installation: $"trim-{i}", version: $"1.0.{i}", ip: "198.51.100.9"))
            .ToList();
        await fixture.QueryAsync(tenantId, db =>
        {
            db.UpdateCheckLogs.AddRange(logs);
            return Task.FromResult(db.SaveChanges());
        });

        // 触发一轮清理：记录时间均为 now，不会被 7 天保留期删除，仅触发每应用 500 条裁剪
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LogRetentionCleanupService>()
            .CleanupAsync(CancellationToken.None);

        var remaining = await fixture.QueryAsync(tenantId, db => db.UpdateCheckLogs
            .Where(l => l.AppKey == scenario.AppBusinessId && l.ClientIp == "198.51.100.9")
            .Select(l => l.CurrentVersion)
            .ToListAsync());
        Assert.Equal(500, remaining.Count);
        Assert.DoesNotContain("1.0.1", remaining);
        Assert.DoesNotContain("1.0.20", remaining);
        Assert.Contains("1.0.21", remaining);
        Assert.Contains("1.0.520", remaining);
    }

    private static UpdateCheckLog MakeLog(Scenario scenario, string installation, string version, string? ip) =>
        new()
        {
            TenantId = scenario.TenantId,
            AppId = scenario.AppId,
            InstallationId = installation,
            ProjectKey = scenario.ProjectKey,
            AppKey = scenario.AppBusinessId,
            Platform = UpdatePlatform.Windows,
            Arch = UpdateArch.X64,
            CurrentVersion = version,
            ClientIp = ip,
            CheckedAt = DateTimeOffset.UtcNow
        };
}
