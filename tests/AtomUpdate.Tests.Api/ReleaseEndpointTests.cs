using System.Net;
using System.Text.Json;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Tests.Api;

/// <summary>Release 生命周期（03 §3.4）：create 幂等 / publish / unpublish / 鉴权范围 / 过滤。</summary>
[Collection("api")]
public class ReleaseEndpointTests(ApiFixture fixture)
{
    private static ReleaseCreateRequest SimpleRelease(string version = "2.0.0") =>
        ApiFixture.WindowsRelease(version, ApiFixture.FullAsset("https://cdn.test/app.zip", ApiFixture.Sha('1')));

    [Fact]
    public async Task Create_SamePayload_IsIdempotent()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var request = ApiFixture.WindowsRelease("2.0.0",
            ApiFixture.FullAsset("https://cdn.test/app-2.0.0.zip", ApiFixture.Sha('1')),
            ApiFixture.DeltaAsset("1.0.0", "https://cdn.test/app-1.0.0-to-2.0.0.delta.zip", ApiFixture.Sha('3')));

        var first = await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, request);
        var second = (await fixture.TryCreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, request))
            .Expect(HttpStatusCode.OK);

        Assert.Equal(first.Id, second.Data.Id); // 资产逐项一致 → 幂等 200
        Assert.Equal(ReleaseStatus.Draft, second.Data.Status);

        var list = (await fixture.SendJsonAsync<PagedResult<ReleaseDto>>(HttpMethod.Get,
                ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(1, list.Data.Total);
    }

    [Fact]
    public async Task Create_SameVersion_WithDifferentAssets_Returns409()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            ApiFixture.WindowsRelease("2.0.0", ApiFixture.FullAsset("https://cdn.test/a.zip", ApiFixture.Sha('1'))));

        var conflicting = ApiFixture.WindowsRelease("2.0.0",
            ApiFixture.FullAsset("https://cdn.test/a.zip", ApiFixture.Sha('9')));

        (await fixture.TryCreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, conflicting))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.ReleaseExists);
    }

    [Fact]
    public async Task WithdrawnRelease_CannotRecreateOrRepublish()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var request = SimpleRelease();
        var release = await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, request);

        (await fixture.UnpublishAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, release.Id))
            .Expect(HttpStatusCode.OK);

        (await fixture.TryCreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, request))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.ReleaseExists);
        (await fixture.PublishAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, release.Id,
                UpdateChannel.Stable))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.ReleaseWithdrawn);
    }

    [Fact]
    public async Task Publish_InvalidChannelValue_Returns400()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var release = await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            SimpleRelease());

        var raw = JsonSerializer.Deserialize<JsonElement>("{\"channels\":[\"nope\"]}");
        var result = await fixture.SendJsonAsync<JsonElement>(HttpMethod.Post,
            $"{ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId)}/{release.Id}/publish", raw,
            bearer: scenario.AdminToken);

        // 非法枚举在请求体反序列化阶段被拒（进入端点前），响应非 JSON 错误体
        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
    }

    [Fact]
    public async Task AnonymousCreate_Returns401()
    {
        var scenario = await fixture.CreateScenarioAsync();

        (await fixture.TryCreateReleaseAsync(string.Empty, scenario.ProjectId, scenario.AppId, SimpleRelease()))
            .ExpectError(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Delete_DraftRelease_RemovesRecord()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var release = await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            ApiFixture.WindowsRelease("2.1.0",
                ApiFixture.FullAsset("https://cdn.test/app-2.1.0.zip", ApiFixture.Sha('2')),
                ApiFixture.DeltaAsset("1.0.0", "https://cdn.test/app.delta.zip", ApiFixture.Sha('3'))));
        var path = $"{ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId)}/{release.Id}";

        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete, path, null, bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.NoContent);

        var list = (await fixture.SendJsonAsync<PagedResult<ReleaseDto>>(HttpMethod.Get,
                ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(0, list.Data.Total);

        // 已删除后同版本可重新创建（物理删除不占用唯一版本）
        var recreated = (await fixture.TryCreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
                ApiFixture.WindowsRelease("2.1.0",
                    ApiFixture.FullAsset("https://cdn.test/app-2.1.0.zip", ApiFixture.Sha('2')))))
            .Expect(HttpStatusCode.Created);
        Assert.Equal(ReleaseStatus.Draft, recreated.Data.Status);
    }

    [Fact]
    public async Task Delete_PublishedRelease_Returns409()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var release = await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, SimpleRelease());
        var path = $"{ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId)}/{release.Id}";

        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete, path, null, bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.Conflict);

        // 撤回后允许删除
        (await fixture.UnpublishAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, release.Id))
            .Expect(HttpStatusCode.OK);
        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete, path, null, bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_MissingRelease_Returns404()
    {
        var scenario = await fixture.CreateScenarioAsync();

        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete,
                $"{ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId)}/999999", null, bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.NotFound, ErrorCodes.ReleaseNotFound);
    }

    [Fact]
    public async Task ManagementSessionToken_CanListAndCreateReleases()
    {
        // Ingest 策略曾按 JwtBearer 方案名判定管理会话，而 JwtBearer 写入的身份类型是
        // AuthenticationTypes.Federation，判定恒 false → 后台"版本发布"页对 JWT 会话一律 403。
        // 会话判定现基于 sub 声明，这里锁住该行为。
        var scenario = await fixture.CreateScenarioAsync();
        var path = ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId);

        var list = (await fixture.SendJsonAsync<PagedResult<ReleaseDto>>(HttpMethod.Get, path,
                bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(0, list.Data.Total);

        var created = (await fixture.SendJsonAsync<ReleaseDto>(HttpMethod.Post, path, SimpleRelease(),
                bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.Created);
        Assert.Equal("2.0.0", created.Data.Version);
    }

    [Fact]
    public async Task Patch_NotesAreRewrittenWholesale_AndValidated()
    {
        // 后台四段编辑器（新增/优化/修复/注意）每次都整体下发 Notes，锁住 PATCH 的"整体重写 + 嵌套校验"语义
        var scenario = await fixture.CreateScenarioAsync();
        var release = await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            SimpleRelease());
        var path = $"{ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId)}/{release.Id}";

        var patched = (await fixture.SendJsonAsync<ReleaseDto>(HttpMethod.Patch, path,
                new ReleaseUpdateRequest
                {
                    Notes = new ReleaseNoteInput
                    {
                        New = ["新增 A", "新增 B"],
                        Improvements = ["优化 C"],
                        Fixes = ["修复 D"],
                        Notes = ["注意 E"]
                    }
                }, bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(new[] { "新增 A", "新增 B" }, patched.Data.Notes!.New);
        Assert.Equal(new[] { "优化 C" }, patched.Data.Notes.Improvements);
        Assert.Equal(new[] { "修复 D" }, patched.Data.Notes.Fixes);
        Assert.Equal(new[] { "注意 E" }, patched.Data.Notes.Notes);

        var reduced = (await fixture.SendJsonAsync<ReleaseDto>(HttpMethod.Patch, path,
                new ReleaseUpdateRequest { Notes = new ReleaseNoteInput { New = ["仅剩这条"] } },
                bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(new[] { "仅剩这条" }, reduced.Data.Notes!.New);
        Assert.Empty(reduced.Data.Notes.Improvements);
        Assert.Empty(reduced.Data.Notes.Fixes);
        Assert.Empty(reduced.Data.Notes.Notes);

        var oversized = new ReleaseUpdateRequest
        {
            Notes = new ReleaseNoteInput { New = Enumerable.Range(0, 101).Select(i => $"l{i}").ToList() }
        };
        (await fixture.SendJsonAsync<ReleaseDto>(HttpMethod.Patch, path, oversized, bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task List_ParsesFiltersCaseInsensitively_AndRejectsInvalidValues()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            ApiFixture.WindowsRelease("2.0.0",
                ApiFixture.FullAsset("https://cdn.test/win-2.0.0-x64.zip", ApiFixture.Sha('1')),
                ApiFixture.FullAsset("https://cdn.test/win-2.0.0-arm64.zip", ApiFixture.Sha('4'),
                    arch: UpdateArch.Arm64)));
        await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            ApiFixture.WindowsRelease("1.5.0", ApiFixture.FullAsset("https://cdn.test/win-1.5.0.zip", ApiFixture.Sha('2'))));

        var path = ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId);

        // 架构过滤 = "有该架构包的版本"（架构在资产上，不在版本上）
        var arm = (await fixture.SendJsonAsync<PagedResult<ReleaseDto>>(HttpMethod.Get,
                $"{path}?arch=arm64", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(1, arm.Data.Total);
        Assert.Equal("2.0.0", arm.Data.Items[0].Version);
        Assert.Equal(2, arm.Data.Items[0].Assets.Count);

        var x64 = (await fixture.SendJsonAsync<PagedResult<ReleaseDto>>(HttpMethod.Get,
                $"{path}?arch=X64", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(2, x64.Data.Total);

        var drafts = (await fixture.SendJsonAsync<PagedResult<ReleaseDto>>(HttpMethod.Get,
                $"{path}?status=Draft", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(2, drafts.Data.Total);

        (await fixture.SendJsonAsync<PagedResult<ReleaseDto>>(HttpMethod.Get,
                $"{path}?arch=osx", bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task Create_SameVersionTwice_OnTheSameApp_ConflictsRegardlessOfArch()
    {
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            ApiFixture.WindowsRelease("2.0.0",
                ApiFixture.FullAsset("https://cdn.test/a-x64.zip", ApiFixture.Sha('1'))));

        // 唯一性回到 (AppId, Version)：同版本补另一个架构的包要走 PATCH，不是新建第二条版本
        (await fixture.TryCreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
                ApiFixture.WindowsRelease("2.0.0",
                    ApiFixture.FullAsset("https://cdn.test/a-arm64.zip", ApiFixture.Sha('2'),
                        arch: UpdateArch.Arm64))))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.ReleaseExists);
    }

    [Fact]
    public async Task Unpublish_IsIdempotent_AndMarksChannelsWithdrawn()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var release = await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, SimpleRelease());

        var first = (await fixture.UnpublishAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, release.Id))
            .Expect(HttpStatusCode.OK).Data;
        var channelRow = Assert.Single(first.Channels); // 发布矩阵 = 版本 × 通道
        Assert.Equal(ReleaseChannelStatus.Withdrawn, channelRow.Status);

        var second = (await fixture.UnpublishAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId, release.Id))
            .Expect(HttpStatusCode.OK).Data;
        Assert.Equal(channelRow.ReleaseSeq, Assert.Single(second.Channels).ReleaseSeq);
    }
}
