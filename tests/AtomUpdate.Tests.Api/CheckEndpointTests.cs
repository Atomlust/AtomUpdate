using System.Net;
using System.Text.Json;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Tests.Api;

/// <summary>/api/check 全场景（03 §3）：分层响应 / ETag / 包选择 / 鉴权 / 多租户 / 异步日志。</summary>
[Collection("api")]
public class CheckEndpointTests(ApiFixture fixture)
{
    private static readonly string FullZipUrl = "https://cdn.test/app-2.0.0.zip";
    private static readonly string FullExeUrl = "https://cdn.test/app-2.0.0-setup.exe";
    private static readonly string DeltaUrl = "https://cdn.test/app-1.0.0-to-2.0.0.delta.zip";

    private static ReleaseCreateRequest RichRelease() => new()
    {
        Version = "2.0.0",
        Assets =
        [
            ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('1')),
            ApiFixture.FullAsset(FullExeUrl, ApiFixture.Sha('2'), PackageType.Exe),
            ApiFixture.DeltaAsset("1.0.0", DeltaUrl, ApiFixture.Sha('3'))
        ],
        Notes = new ReleaseNoteInput
        {
            Language = "en",
            New = ["全新同步引擎"],
            Fixes = ["修复断点续传"]
        }
    };

    // ---------- 分层响应 ----------

    [Fact]
    public async Task NoRelease_ReturnsStableProjection_WithPolicyBody()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var result = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", policyVersion: 0)))
            .Expect(HttpStatusCode.OK);

        var data = result.Data;
        Assert.False(data.HasUpdate);
        Assert.Equal("1.0.0", data.LatestVersion);
        Assert.False(data.PolicyUnchanged);
        Assert.NotNull(data.Policy);
        Assert.Equal(1, data.Policy!.Version); // 应用创建时预置策略 v1
        Assert.True(data.Policy.PollingEnabled);
        Assert.Equal(360, data.Policy.PollingIntervalMinutes);
        Assert.True(result.Root.TryGetProperty("policy", out _));
        result.ExpectNoField("package");
        result.ExpectNoField("releaseNotes");
    }

    [Fact]
    public async Task MatchingPolicyVersion_OmitsPolicyBody()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var result = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", policyVersion: 1)))
            .Expect(HttpStatusCode.OK);

        Assert.True(result.Data.PolicyUnchanged);
        Assert.Null(result.Data.Policy);
        result.ExpectNoField("policy");
    }

    [Theory]
    [InlineData("1.0")] // NuGet SemanticVersion 要求三段式
    [InlineData("not-a-version")]
    public async Task InvalidCurrentVersion_Returns400_InvalidVersion(string version)
    {
        var scenario = await fixture.CreateScenarioAsync();

        var result = await fixture.CheckAsync(ApiFixture.CheckBody(scenario, version));

        result.ExpectError(HttpStatusCode.BadRequest, ErrorCodes.InvalidVersion);
    }

    // ---------- 资源定位错误 ----------

    [Fact]
    public async Task UnknownProject_Returns404()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var result = await fixture.CheckAsync(
            ApiFixture.CheckBody(scenario, "1.0.0", projectKey: $"p-missing-{Guid.NewGuid():N}"));

        result.ExpectError(HttpStatusCode.NotFound, ErrorCodes.ProjectNotFound);
    }

    [Fact]
    public async Task UnknownApp_Returns404()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var result = await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", appId: "com.missing.app"));

        result.ExpectError(HttpStatusCode.NotFound, ErrorCodes.AppNotFound);
    }

    [Fact]
    public async Task InvalidChannelValue_Returns400()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var body = JsonSerializer.Serialize(ApiFixture.CheckBody(scenario, "1.0.0"), ApiFixture.Json);
        var raw = JsonSerializer.Deserialize<JsonElement>(body[..^1] + ",\"channel\":\"nope\"}");
        var result = await fixture.SendJsonAsync<JsonElement>(HttpMethod.Post, ApiRoutes.Check, raw);

        // 非法枚举在请求体反序列化阶段被拒（进入端点前），响应非 JSON 错误体
        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
    }

    // ---------- 鉴权（check 公开，歧义 projectKey fail-closed） ----------

    [Fact]
    public async Task AmbiguousProjectKey_AcrossTenants_Returns401()
    {
        var first = await fixture.CreateScenarioAsync();
        await fixture.CreateScenarioAsync(projectKey: first.ProjectKey);

        (await fixture.CheckAsync(ApiFixture.CheckBody(first, "1.0.0")))
            .ExpectError(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    // ---------- 包选择 ----------

    [Fact]
    public async Task PublishedRelease_ServesDeltaFirst_FullPackageIsInstaller()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, RichRelease());

        var result = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        var data = result.Data;

        Assert.True(data.HasUpdate);
        Assert.Equal("2.0.0", data.LatestVersion);
        Assert.NotNull(data.Package);
        Assert.Equal(AssetKind.Delta, data.Package!.Type);
        Assert.Equal("1.0.0", data.Package.FromVersion);
        Assert.Equal(DeltaUrl, data.Package.Url);

        // fullPackage 恒为安装器优先（非 zip），Type 字段整体省略
        Assert.NotNull(data.FullPackage);
        Assert.Null(data.FullPackage!.Type);
        Assert.Equal(PackageType.Exe, data.FullPackage.PackageType);
        Assert.Equal(FullExeUrl, data.FullPackage.Url);

        Assert.Equal("delta", result.Root.GetProperty("package").GetProperty("type").GetString());
        Assert.False(result.Root.GetProperty("fullPackage").TryGetProperty("type", out _));

        Assert.NotNull(data.ReleaseNotes);
        Assert.Equal(new[] { "全新同步引擎" }, data.ReleaseNotes!.New);
        Assert.Equal(new[] { "修复断点续传" }, data.ReleaseNotes.Fixes);
    }

    [Fact]
    public async Task NoDeltaMatch_FallsBackToFullZipPackage()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, RichRelease());

        var result = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.5.0"))).Expect(HttpStatusCode.OK);
        var data = result.Data;

        Assert.True(data.HasUpdate);
        Assert.Equal(AssetKind.Full, data.Package!.Type);
        Assert.Equal(FullZipUrl, data.Package.Url); // delta 未命中 → 全量 zip 优先
        Assert.Null(data.Package.FromVersion);
    }

    [Fact]
    public async Task BuildMetadataVersion_Normalizes_AndMatchesDelta()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, RichRelease());

        var result = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0+build.5"))).Expect(HttpStatusCode.OK);

        Assert.Equal(AssetKind.Delta, result.Data.Package!.Type);
        Assert.Equal("1.0.0", result.Data.Package.FromVersion); // 归一化后命中 delta 起点
    }

    [Fact]
    public async Task PlatformMismatch_Returns400_PlatformMismatch()
    {
        var scenario = await fixture.CreateScenarioAsync();

        var result = await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0",
            platform: UpdatePlatform.Macos));

        result.ExpectError(HttpStatusCode.BadRequest, ErrorCodes.PlatformMismatch);
    }

    [Fact]
    public async Task Releases_AreIsolatedPerApp_PlatformComesFromApp()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('1'))));

        var macApp = await fixture.CreateAppAsync(scenario.AdminToken, scenario.ProjectId,
            $"com.macos.{Guid.NewGuid():N}"[..24], UpdatePlatform.Macos);
        var macRelease = await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, macApp.Id,
            new ReleaseCreateRequest
            {
                Version = "2.5.0",
                Assets = [ApiFixture.FullAsset("https://cdn.test/app-2.5.0.dmg", ApiFixture.Sha('4'), PackageType.Dmg)]
            });
        await fixture.PublishAsync(scenario.AdminToken, scenario.ProjectId, macApp.Id, macRelease.Id,
            UpdateChannel.Stable);

        var windows = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "2.5.0"))).Expect(HttpStatusCode.OK);
        Assert.Equal("2.0.0", windows.Data.LatestVersion);
        Assert.False(windows.Data.HasUpdate); // 2.5.0 属于另一个应用，不跨应用可见

        var mac = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0",
            platform: UpdatePlatform.Macos, appId: macApp.AppId))).Expect(HttpStatusCode.OK);
        Assert.True(mac.Data.HasUpdate);
        Assert.Equal("2.5.0", mac.Data.LatestVersion);
        Assert.Equal(PackageType.Dmg, mac.Data.Package!.PackageType);
    }

    [Fact]
    public async Task Assets_PerArch_Arm64FallsBackToX64_WhenNoArm64Package()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, new ReleaseCreateRequest
        {
            Version = "2.0.0",
            Assets =
            [
                ApiFixture.FullAsset("https://cdn.test/app-2.0.0-x64.zip", ApiFixture.Sha('1')),
                ApiFixture.FullAsset("https://cdn.test/app-2.0.0-arm64.zip", ApiFixture.Sha('2'),
                    arch: UpdateArch.Arm64)
            ]
        });

        var arm = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0",
            arch: UpdateArch.Arm64))).Expect(HttpStatusCode.OK);
        Assert.True(arm.Data.HasUpdate);
        Assert.Equal("https://cdn.test/app-2.0.0-arm64.zip", arm.Data.Package!.Url);
        Assert.Equal(UpdateArch.Arm64, arm.Data.Package!.Arch);

        var x64 = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.Equal("https://cdn.test/app-2.0.0-x64.zip", x64.Data.Package!.Url);
        Assert.Equal(UpdateArch.X64, x64.Data.Package!.Arch);
    }

    [Fact]
    public async Task Assets_Arm64Device_FallsBackToX64Package_WithPackageArch()
    {
        // 只发 x64 包：arm64 客户端回落 x64 包，且响应带包架构（验签口径）
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, new ReleaseCreateRequest
        {
            Version = "2.0.0",
            Assets = [ApiFixture.FullAsset("https://cdn.test/app-2.0.0-x64.zip", ApiFixture.Sha('1'),
                arch: UpdateArch.X64)]
        });

        var arm = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0",
            arch: UpdateArch.Arm64))).Expect(HttpStatusCode.OK);
        Assert.True(arm.Data.HasUpdate);
        Assert.Equal("https://cdn.test/app-2.0.0-x64.zip", arm.Data.Package!.Url);
        Assert.Equal(UpdateArch.X64, arm.Data.Package!.Arch);
    }

    [Fact]
    public async Task MissingRequestedArch_IsTreated_AsNoUpdate()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, new ReleaseCreateRequest
        {
            Version = "2.0.0",
            Assets = [ApiFixture.FullAsset("https://cdn.test/app-2.0.0-x64.zip", ApiFixture.Sha('1'),
                arch: UpdateArch.X64)]
        });

        // x86 客户端没有 x64 回落（只有 arm64→x64 单向回落），宁可报无更新也不发错包
        var result = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0",
            arch: UpdateArch.X86))).Expect(HttpStatusCode.OK);

        Assert.False(result.Data.HasUpdate);
        Assert.Equal("2.0.0", result.Data.LatestVersion);
    }

    // ---------- 通道语义 ----------

    [Fact]
    public async Task Downgrade_OnlyAllowed_WhenPolicyAllows()
    {
        var strict = await fixture.CreateScenarioAsync();
        var permissive = await fixture.CreateScenarioAsync(allowDowngrade: true);

        foreach (var scenario in new[] { strict, permissive })
        {
            await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
                ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('1'))));
        }

        var strictCheck = (await fixture.CheckAsync(ApiFixture.CheckBody(strict, "3.0.0"))).Expect(HttpStatusCode.OK);
        Assert.False(strictCheck.Data.HasUpdate); // 默认防降级

        var permissiveCheck = (await fixture.CheckAsync(ApiFixture.CheckBody(permissive, "3.0.0")))
            .Expect(HttpStatusCode.OK);
        Assert.True(permissiveCheck.Data.HasUpdate); // 策略显式开启才提示回退
        Assert.Equal("2.0.0", permissiveCheck.Data.LatestVersion);
    }

    [Fact]
    public async Task ForceUpdate_RequiresPolicyGate_And_MinSupportedFloor()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, new ReleaseCreateRequest
        {
            Version = "2.6.0",
            ForceUpdate = true,
            Assets = [ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('1'))]
        });

        var gated = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "2.0.0"))).Expect(HttpStatusCode.OK);
        Assert.True(gated.Data.HasUpdate);
        Assert.False(gated.Data.ForceUpdate); // 策略未开放强制更新

        await fixture.UpdatePolicyAsync(scenario.AdminToken, scenario.AppId,
            new PolicyUpdateRequest { AllowForceUpdate = true });

        var forced = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "2.0.0"))).Expect(HttpStatusCode.OK);
        Assert.True(forced.Data.ForceUpdate);

        // minSupportedVersion 地板：即使 release 未标 force，低于地板也强制
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, new ReleaseCreateRequest
        {
            Version = "3.0.0",
            MinSupportedVersion = "2.5.0",
            Assets = [ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('5'))]
        });

        var belowFloor = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "2.0.0"))).Expect(HttpStatusCode.OK);
        Assert.Equal("3.0.0", belowFloor.Data.LatestVersion);
        Assert.Equal("2.5.0", belowFloor.Data.MinSupportedVersion);
        Assert.True(belowFloor.Data.ForceUpdate);

        var aboveFloor = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "2.6.0"))).Expect(HttpStatusCode.OK);
        Assert.False(aboveFloor.Data.ForceUpdate);
    }

    [Fact]
    public async Task PublishChannel_IsolatesVisibility()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var release = await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('1'))));
        (await fixture.PublishAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, release.Id,
            UpdateChannel.Dev)).Expect(HttpStatusCode.OK);

        var stable = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0")))
            .Expect(HttpStatusCode.OK);
        Assert.False(stable.Data.HasUpdate); // 仅发布到 dev

        var dev = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", channel: UpdateChannel.Dev)))
            .Expect(HttpStatusCode.OK);
        Assert.True(dev.Data.HasUpdate);
    }

    [Fact]
    public async Task Unpublish_FallsBackToPreviousPublishedVersion()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("2.5.0", ApiFixture.FullAsset("https://cdn.test/app-2.5.0.zip", ApiFixture.Sha('6'))));
        var v26 = await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("2.6.0", ApiFixture.FullAsset("https://cdn.test/app-2.6.0.zip", ApiFixture.Sha('7'))));

        var before = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.Equal("2.6.0", before.Data.LatestVersion);

        (await fixture.UnpublishAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, v26.Id))
            .Expect(HttpStatusCode.OK);

        var after = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.Equal("2.5.0", after.Data.LatestVersion);
        Assert.NotEqual(before.ETag, after.ETag);
    }

    // ---------- ETag / 304 ----------

    [Fact]
    public async Task ETag_RoundTrip_304_And_Wildcard()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('1'))));

        var first = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.False(string.IsNullOrEmpty(first.ETag));

        var revalidated = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"), ifNoneMatch: first.ETag))
            .Expect(HttpStatusCode.NotModified);
        Assert.Equal(string.Empty, revalidated.Body); // 304 无响应体

        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"), ifNoneMatch: "*"))
            .Expect(HttpStatusCode.NotModified);

        var mismatched = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"), ifNoneMatch: "\"0000000000000000\""))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(first.ETag, mismatched.ETag);
    }

    [Fact]
    public async Task ETag_Changes_OnPolicyBumpAndPublish_NotOnNoOpPatch()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('1'))));

        var initial = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);

        var bumped = await fixture.UpdatePolicyAsync(scenario.AdminToken, scenario.AppId,
            new PolicyUpdateRequest { PollingIntervalMinutes = 999 });
        Assert.Equal(2, bumped.Version);

        var afterBump = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.NotEqual(initial.ETag, afterBump.ETag);
        Assert.Equal(2, afterBump.Data.Policy!.Version);
        Assert.False(afterBump.Data.PolicyUnchanged);

        // 无实际变化的 PATCH 不递增策略版本（ETag 不变）
        var noop = await fixture.UpdatePolicyAsync(scenario.AdminToken, scenario.AppId,
            new PolicyUpdateRequest { PollingIntervalMinutes = 999 });
        Assert.Equal(2, noop.Version);
        var afterNoop = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.Equal(afterBump.ETag, afterNoop.ETag);

        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("3.0.0", ApiFixture.FullAsset("https://cdn.test/app-3.0.0.zip", ApiFixture.Sha('8'))));
        var afterPublish = (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0"))).Expect(HttpStatusCode.OK);
        Assert.NotEqual(afterNoop.ETag, afterPublish.ETag);
        Assert.Equal("3.0.0", afterPublish.Data.LatestVersion);
    }

    // ---------- 异步检查日志 ----------

    [Fact]
    public async Task CheckLog_Written_Asynchronously_WithEtagHitFlags()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset(FullZipUrl, ApiFixture.Sha('1'))));
        var installationId = Guid.NewGuid().ToString("D");

        var first = (await fixture.CheckAsync(
            ApiFixture.CheckBody(scenario, "1.0.0+build.5", installationId: installationId))).Expect(HttpStatusCode.OK);
        (await fixture.CheckAsync(
                ApiFixture.CheckBody(scenario, "1.0.0+build.5", installationId: installationId),
                ifNoneMatch: first.ETag))
            .Expect(HttpStatusCode.NotModified);

        await fixture.WaitForDbAsync(scenario.TenantId, async db => await db.UpdateCheckLogs.AsNoTracking()
            .CountAsync(log => log.InstallationId == installationId) >= 2);

        var rows = await fixture.QueryAsync(scenario.TenantId, db => db.UpdateCheckLogs.AsNoTracking()
            .Where(log => log.InstallationId == installationId)
            .ToListAsync());

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(scenario.TenantId, row.TenantId);
            Assert.Equal(scenario.ProjectKey, row.ProjectKey);
            Assert.Equal(scenario.AppBusinessId, row.AppKey);
            Assert.Equal("1.0.0", row.CurrentVersion); // 构建元数据已归一化
            Assert.Equal("2.0.0", row.LatestVersion);
            Assert.True(row.HasUpdate);
            Assert.Equal(UpdateChannel.Stable, row.Channel);
            Assert.False(row.PolicyUnchanged);
        });
        Assert.Single(rows, row => row.ETagHit);
        Assert.Single(rows, row => !row.ETagHit);
    }

    [Fact]
    public async Task CheckLog_NotWritten_ForErrorResponses()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var errorInstallation = Guid.NewGuid().ToString("D");

        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "bogus", installationId: errorInstallation)))
            .ExpectError(HttpStatusCode.BadRequest, ErrorCodes.InvalidVersion);
        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", projectKey: $"p-missing-{Guid.NewGuid():N}",
            installationId: errorInstallation))).ExpectError(HttpStatusCode.NotFound, ErrorCodes.ProjectNotFound);

        // 先用一次合法检查等到写入器完成一轮落库，确认队列被消费后再断言错误请求无日志
        var okInstallation = Guid.NewGuid().ToString("D");
        (await fixture.CheckAsync(ApiFixture.CheckBody(scenario, "1.0.0", installationId: okInstallation)))
            .Expect(HttpStatusCode.OK);
        await fixture.WaitForDbAsync(scenario.TenantId, db => db.UpdateCheckLogs.AsNoTracking()
            .AnyAsync(log => log.InstallationId == okInstallation));

        var errorRows = await fixture.QueryAsync(scenario.TenantId, db => db.UpdateCheckLogs.AsNoTracking()
            .CountAsync(log => log.InstallationId == errorInstallation));
        Assert.Equal(0, errorRows);
    }
}
