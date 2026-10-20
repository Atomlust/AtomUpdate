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
        // 时区默认东八区（IANA 标识）
        Assert.Equal(DisplayTimeZone.DefaultTimeZoneId, dto.Data.TimeZoneId);
    }

    [Fact]
    public async Task Update_TimeZone_Persists_AndRejectsUnknownId()
    {
        var token = await LoginAsync();

        var updated = (await fixture.SendJsonAsync<SettingsDto>(HttpMethod.Patch, SettingsPath,
                new SettingsUpdateRequest { TimeZoneId = "America/New_York" }, bearer: token))
            .Expect(HttpStatusCode.OK);
        Assert.Equal("America/New_York", updated.Data.TimeZoneId);

        var readBack = (await fixture.SendJsonAsync<SettingsDto>(HttpMethod.Get, SettingsPath, bearer: token))
            .Expect(HttpStatusCode.OK);
        Assert.Equal("America/New_York", readBack.Data.TimeZoneId);

        // 不可识别标识必须拒绝，避免存进去后前端静默回落到东八区
        (await fixture.SendJsonAsync<object>(HttpMethod.Patch, SettingsPath,
                new SettingsUpdateRequest { TimeZoneId = "Not/AZone" }, bearer: token))
            .Expect(HttpStatusCode.BadRequest);

        // 还原默认
        await fixture.SendJsonAsync<SettingsDto>(HttpMethod.Patch, SettingsPath,
            new SettingsUpdateRequest { TimeZoneId = DisplayTimeZone.DefaultTimeZoneId }, bearer: token);
    }

    [Fact]
    public async Task DisplayTimeZone_ReadableByTenantUser_AndDefaultsToEast8()
    {
        // 租户成员不是平台管理员，但同样需要按平台时区显示时间，不能 403
        var userToken = fixture.MintUserToken(tenantId: 24681);
        var dto = (await fixture.SendJsonAsync<DisplayTimeZoneDto>(HttpMethod.Get,
                ApiRoutes.ManageDisplayTimeZone, bearer: userToken))
            .Expect(HttpStatusCode.OK);

        Assert.Equal(DisplayTimeZone.DefaultTimeZoneId, dto.Data.TimeZoneId);

        // 未设置时回落东八区：解析出的时区偏移必须是 +08:00（容器本身跑在 UTC 上，
        // 若回落逻辑坏掉会退化成 UTC 偏移 0，正是旧 ToLocalTime() 的线上症状）
        var zone = DisplayTimeZone.Resolve(dto.Data.TimeZoneId);
        Assert.Equal(TimeSpan.FromHours(8), zone.BaseUtcOffset);
        Assert.EndsWith("+08:00", TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).ToString("zzz"));
    }

    [Fact]
    public async Task DisplayTimeZone_RequiresAuthentication()
    {
        (await fixture.SendJsonAsync<object>(HttpMethod.Get, ApiRoutes.ManageDisplayTimeZone))
            .Expect(HttpStatusCode.Unauthorized);
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
