using System.Net;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Tests.Api;

/// <summary>读路径缓存行为（02 §3.1）：解析/版本缓存命中不返回脏数据——发布/撤回/策略/项目/应用变更
 /// 经主动失效立即对 /api/check 与概览可见；缓存命中路径每次请求恰好落一条检查日志。</summary>
[Collection("api")]
public class CacheBehaviorTests(ApiFixture fixture)
{
    [Fact]
    public async Task Publish_IsVisibleImmediately_DespiteWarmCaches()
    {
        var scenario = await fixture.CreateScenarioAsync();

        // 预热：项目/应用/策略解析 + 「该通道无已发布版本」全部进缓存
        var before = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.False(before.Data.HasUpdate);
        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);

        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset("https://cdn.test/app-2.0.0.zip", ApiFixture.Sha('9'))));

        // 发布端点已主动失效，无需等待 TTL 即可见
        var after = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.True(after.Data.HasUpdate);
        Assert.Equal("2.0.0", after.Data.LatestVersion);
        Assert.NotEqual(before.ETag, after.ETag);
    }

    [Fact]
    public async Task PolicyUpdate_IsVisibleImmediately_DespiteWarmCaches()
    {
        var scenario = await fixture.CreateScenarioAsync();

        // 预热策略缓存（policyVersion=1 → 策略体省略）
        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", policyVersion: 1)))
            .Expect(HttpStatusCode.OK);

        var bumped = await fixture.UpdatePolicyAsync(scenario.AdminToken, scenario.AppId,
            new PolicyUpdateRequest { PollingIntervalMinutes = 777 });
        Assert.Equal(2, bumped.Version);

        var after = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", policyVersion: 1)))
            .Expect(HttpStatusCode.OK);
        Assert.False(after.Data.PolicyUnchanged);
        Assert.Equal(2, after.Data.Policy!.Version);
        Assert.Equal(777, after.Data.Policy.PollingIntervalMinutes);
    }

    [Fact]
    public async Task ProjectCreatedAfterNegativeLookup_IsVisibleImmediately()
    {
        // 以不存在的 projectKey 检查（缓存负解析结果）
        var key = $"pcache-{Guid.NewGuid():N}";
        var miss = await fixture.CheckAsync(new CheckRequest
        {
            ProjectKey = key,
            AppId = "com.ghost.app",
            CurrentVersion = "1.0.0",
            Platform = UpdatePlatform.Windows,
            Arch = UpdateArch.X64,
            InstallationId = Guid.NewGuid().ToString("D")
        });
        miss.ExpectError(HttpStatusCode.NotFound, ErrorCodes.ProjectNotFound);

        // 随后创建同 key 项目与应用：项目创建端点失效解析缓存，立即命中
        var scenario = await fixture.CreateScenarioAsync(projectKey: key);
        var hit = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.False(hit.Data.HasUpdate);
    }

    [Fact]
    public async Task AppCreatedAfterNegativeLookup_IsVisibleImmediately()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var lateAppId = $"com.late.{Guid.NewGuid():N}"[..24];

        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", appId: lateAppId)))
            .ExpectError(HttpStatusCode.NotFound, ErrorCodes.AppNotFound);

        await fixture.CreateAppAsync(scenario.AdminToken, scenario.ProjectId, lateAppId);

        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", appId: lateAppId)))
            .Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CachedCheckPath_WritesExactlyOneLogPerRequest()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset("https://cdn.test/app-2.0.0.zip", ApiFixture.Sha('9'))));
        var installationId = Guid.NewGuid().ToString("D");

        // 首查回源 + 3 次全缓存命中（200）+ 1 次 ETag 重验证（304）
        var first = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", installationId: installationId)))
            .Expect(HttpStatusCode.OK);
        for (var i = 0; i < 3; i++)
        {
            (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", installationId: installationId)))
                .Expect(HttpStatusCode.OK);
        }

        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", installationId: installationId),
                ifNoneMatch: first.ETag))
            .Expect(HttpStatusCode.NotModified);

        await fixture.WaitForDbAsync(scenario.TenantId, async db => await db.UpdateCheckLogs.AsNoTracking()
            .CountAsync(log => log.InstallationId == installationId) >= 5);

        var count = await fixture.QueryAsync(scenario.TenantId, db => db.UpdateCheckLogs.AsNoTracking()
            .CountAsync(log => log.InstallationId == installationId));
        Assert.Equal(5, count); // 缓存命中不重复写检查日志：每次请求恰好一条
    }

    [Fact]
    public async Task Overview_IsCached_ButInvalidatedOnAppMutation()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var path = $"{ApiRoutes.ManagePrefix}/overview";

        var first = (await fixture.SendJsonAsync<OverviewDto>(HttpMethod.Get, path, bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(1, first.Apps);

        // 命中缓存
        var second = (await fixture.SendJsonAsync<OverviewDto>(HttpMethod.Get, path, bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(1, second.Apps);

        // 应用创建主动失效概览缓存，不等待 TTL
        await fixture.CreateAppAsync(scenario.AdminToken, scenario.ProjectId, $"com.ov.{Guid.NewGuid():N}"[..24]);
        var third = (await fixture.SendJsonAsync<OverviewDto>(HttpMethod.Get, path, bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(2, third.Apps);
    }
}
