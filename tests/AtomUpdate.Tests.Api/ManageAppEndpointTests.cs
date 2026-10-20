using System.Net;
using System.Text.Json;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Tests.Api;

/// <summary>跨项目应用列表（后台"应用"页默认视图）：默认全量、项目/类型/关键字过滤、所属项目回填、租户隔离。</summary>
[Collection("api")]
public class ManageAppEndpointTests(ApiFixture fixture)
{
    private const string AppsPath = $"{ApiRoutes.ManagePrefix}/apps";

    private static string NewKey() => Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task ListAcrossProjects_ReturnsAllTenantAppsWithOwningProject()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var otherProject = await fixture.CreateProjectAsync(scenario.AdminToken, NewKey());
        await fixture.CreateAppAsync(scenario.AdminToken, otherProject.Id, "com.other.app");

        var list = (await fixture.SendJsonAsync<PagedResult<AppDto>>(HttpMethod.Get, AppsPath,
                bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);

        Assert.Equal(2, list.Data.Total);
        Assert.Contains(list.Data.Items,
            a => a.AppId == scenario.AppBusinessId && a.ProjectId == scenario.ProjectId &&
                 a.ProjectName == scenario.ProjectKey);
        Assert.Contains(list.Data.Items,
            a => a.AppId == "com.other.app" && a.ProjectId == otherProject.Id && a.ProjectName == otherProject.Key);
    }

    [Fact]
    public async Task ListAcrossProjects_FiltersByProjectTypeAndKeyword()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var otherProject = await fixture.CreateProjectAsync(scenario.AdminToken, NewKey());
        await fixture.CreateAppAsync(scenario.AdminToken, otherProject.Id, "com.other.app");
        var console = (await fixture.SendJsonAsync<AppDto>(HttpMethod.Post,
                $"{ApiRoutes.ManagePrefix}/projects/{scenario.ProjectId}/apps",
                new AppCreateRequest
                {
                    AppId = "com.console.app", Name = "DeltaConsole", Type = AppType.Console,
                    Platform = UpdatePlatform.Windows
                },
                bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.Created).Data;

        var byProject = (await fixture.SendJsonAsync<PagedResult<AppDto>>(HttpMethod.Get,
                $"{AppsPath}?projectId={otherProject.Id}", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(1, byProject.Data.Total);
        Assert.Equal("com.other.app", byProject.Data.Items[0].AppId);

        var byType = (await fixture.SendJsonAsync<PagedResult<AppDto>>(HttpMethod.Get,
                $"{AppsPath}?type=console", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(1, byType.Data.Total);
        Assert.Equal(console.Id, byType.Data.Items[0].Id);

        // 关键字同时匹配名称与 AppId（ILike，大小写不敏感）
        var byName = (await fixture.SendJsonAsync<PagedResult<AppDto>>(HttpMethod.Get,
                $"{AppsPath}?keyword=delta", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(1, byName.Data.Total);
        Assert.Equal("DeltaConsole", byName.Data.Items[0].Name);

        var byAppId = (await fixture.SendJsonAsync<PagedResult<AppDto>>(HttpMethod.Get,
                $"{AppsPath}?keyword=com.other", bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(1, byAppId.Data.Total);

        var combined = (await fixture.SendJsonAsync<PagedResult<AppDto>>(HttpMethod.Get,
                $"{AppsPath}?projectId={scenario.ProjectId}&type=desktop&keyword=delta",
                bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(0, combined.Data.Total);
    }

    [Fact]
    public async Task Create_WithoutPlatform_Returns400()
    {
        var scenario = await fixture.CreateScenarioAsync();

        // 平台是应用的一等属性：版本不再带平台，创建应用时必须定下它
        (await fixture.SendJsonAsync<AppDto>(HttpMethod.Post,
                $"{ApiRoutes.ManagePrefix}/projects/{scenario.ProjectId}/apps",
                new AppCreateRequest { AppId = "com.noplatform.app", Name = "NoPlatform" },
                bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task ListAcrossProjects_InvalidType_Returns400()
    {
        var scenario = await fixture.CreateScenarioAsync();

        (await fixture.SendJsonAsync<PagedResult<AppDto>>(HttpMethod.Get, $"{AppsPath}?type=bogus",
                bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task ListAcrossProjects_IsScopedToCallerTenant()
    {
        var mine = await fixture.CreateScenarioAsync();
        var theirs = await fixture.CreateScenarioAsync();

        var list = (await fixture.SendJsonAsync<PagedResult<AppDto>>(HttpMethod.Get, AppsPath,
                bearer: mine.AdminToken))
            .Expect(HttpStatusCode.OK);

        Assert.Equal(1, list.Data.Total);
        Assert.Equal(mine.ProjectId, list.Data.Items[0].ProjectId);
        Assert.DoesNotContain(theirs.AppBusinessId, list.Data.Items.Select(a => a.AppId));
    }

    [Fact]
    public async Task Delete_AppWithoutReleases_Succeeds_AndBlocksWhenReleasesExist()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var path = $"{ApiRoutes.ManagePrefix}/projects/{scenario.ProjectId}/apps/{scenario.AppId}";

        // 有版本（草稿也算）→ 409 先删版本
        await fixture.CreateReleaseAsync(scenario.AdminToken, scenario.ProjectId, scenario.AppId,
            ApiFixture.WindowsRelease("3.0.0",
                ApiFixture.FullAsset("https://cdn.test/app-3.0.0.zip", ApiFixture.Sha('4'))));
        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete, path, null, bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.Conflict);

        // 删掉版本后 → 204，应用消失
        var releases = (await fixture.SendJsonAsync<PagedResult<ReleaseDto>>(HttpMethod.Get,
                ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId), bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.OK);
        foreach (var release in releases.Data.Items)
        {
            (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete,
                    $"{ApiFixture.ReleasePath(scenario.ProjectId, scenario.AppId)}/{release.Id}", null,
                    bearer: scenario.AdminToken))
                .Expect(HttpStatusCode.NoContent);
        }

        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete, path, null, bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.NoContent);

        (await fixture.SendJsonAsync<AppDto>(HttpMethod.Get, $"{ApiRoutes.ManagePrefix}/apps/{scenario.AppId}",
                bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.NotFound, ErrorCodes.AppNotFound);
    }

    [Fact]
    public async Task Delete_MissingApp_Returns404()
    {
        var scenario = await fixture.CreateScenarioAsync();

        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete,
                $"{ApiRoutes.ManagePrefix}/projects/{scenario.ProjectId}/apps/999999", null,
                bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.NotFound, ErrorCodes.AppNotFound);
    }

    [Fact]
    public async Task Delete_ProjectWithoutApps_Succeeds_AndBlocksWhenAppsExist()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var projectPath = $"{ApiRoutes.ManagePrefix}/projects/{scenario.ProjectId}";

        // 项目下有应用 → 409 先删应用
        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete, projectPath, null, bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.Conflict);

        await DeleteAppAsync(scenario);

        // 应用清空后 → 204，项目消失
        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete, projectPath, null, bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.NoContent);

        (await fixture.SendJsonAsync<ProjectDto>(HttpMethod.Get, projectPath, bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.NotFound, ErrorCodes.ProjectNotFound);
    }

    [Fact]
    public async Task Delete_MissingProject_Returns404()
    {
        var scenario = await fixture.CreateScenarioAsync();

        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete,
                $"{ApiRoutes.ManagePrefix}/projects/999999", null, bearer: scenario.AdminToken))
            .ExpectError(HttpStatusCode.NotFound, ErrorCodes.ProjectNotFound);
    }

    private async Task DeleteAppAsync(Scenario scenario)
    {
        (await fixture.SendJsonAsync<JsonElement>(HttpMethod.Delete,
                $"{ApiRoutes.ManagePrefix}/projects/{scenario.ProjectId}/apps/{scenario.AppId}", null,
                bearer: scenario.AdminToken))
            .Expect(HttpStatusCode.NoContent);
    }
}
