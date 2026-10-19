using System.Net;
using AtomUpdate.Api.Settings;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Tests.Api;

/// <summary>系统设置端点：DB 有值优先于代码默认值、平台管理员权限收窄、清理参数生效语义。</summary>
[Collection("api")]
public class SettingsEndpointTests(ApiFixture fixture)
{
    private const string SettingsPath = $"{ApiRoutes.ManagePrefix}/settings";

    private async Task<string> LoginAsync()
    {
        var login = (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = ApiFixture.BootstrapEmail, Password = ApiFixture.BootstrapPassword }))
            .Expect(HttpStatusCode.OK);
        return login.Data.Token;
    }

    [Fact]
    public async Task Get_ReturnsDefaults_WhenNothingSet()
    {
        var token = await LoginAsync();
        var dto = (await fixture.SendJsonAsync<SettingsDto>(HttpMethod.Get, SettingsPath, bearer: token))
            .Expect(HttpStatusCode.OK);

        Assert.Equal(SettingsService.DefaultRetentionDays, dto.Data.RetentionDays);
        Assert.Equal(SettingsService.DefaultCheckLogPerAppLimit, dto.Data.CheckLogPerAppLimit);
    }

    [Fact]
    public async Task Update_Persists_ToDb_AndReturnsNewValues()
    {
        var token = await LoginAsync();
        var updated = (await fixture.SendJsonAsync<SettingsDto>(HttpMethod.Patch, SettingsPath,
                new SettingsUpdateRequest { RetentionDays = 14, CheckLogPerAppLimit = 1000 }, bearer: token))
            .Expect(HttpStatusCode.OK);

        Assert.Equal(14, updated.Data.RetentionDays);
        Assert.Equal(1000, updated.Data.CheckLogPerAppLimit);

        // 读回一致（DB 优先），再改回默认，避免影响其他用例
        var readBack = (await fixture.SendJsonAsync<SettingsDto>(HttpMethod.Get, SettingsPath, bearer: token))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(14, readBack.Data.RetentionDays);

        await fixture.SendJsonAsync<SettingsDto>(HttpMethod.Patch, SettingsPath,
            new SettingsUpdateRequest { RetentionDays = SettingsService.DefaultRetentionDays, CheckLogPerAppLimit = SettingsService.DefaultCheckLogPerAppLimit },
            bearer: token);
        var reverted = (await fixture.SendJsonAsync<SettingsDto>(HttpMethod.Get, SettingsPath, bearer: token))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(SettingsService.DefaultRetentionDays, reverted.Data.RetentionDays);
    }

    [Fact]
    public async Task Update_ByTenantUser_Returns403_PlatformAdminOnly()
    {
        var userToken = fixture.MintUserToken(tenantId: 24681);
        (await fixture.SendJsonAsync<object>(HttpMethod.Patch, SettingsPath,
                new SettingsUpdateRequest { RetentionDays = 30 }, bearer: userToken))
            .Expect(HttpStatusCode.Forbidden);
    }
}
