using System.Net;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Tests.Api;

/// <summary>首次部署设置向导（docs/09 零配置）：空库可初始化并直接建立会话，已初始化一律 409。</summary>
[Collection("api-setup")]
public class SetupEndpointTests(SetupApiFixture fixture)
{
    private const string StatusPath = $"{ApiRoutes.ManagePrefix}/auth/setup/status";
    private const string SetupPath = $"{ApiRoutes.ManagePrefix}/auth/setup";

    [Fact]
    public async Task Setup_OnEmptyDatabase_CreatesPlatformAdmin_AndIssuesWorkingSession()
    {
        // 空库：状态端点报告需要初始化
        var status = (await fixture.SendJsonAsync<SetupStatusResponse>(HttpMethod.Get, StatusPath))
            .Expect(HttpStatusCode.OK);
        Assert.True(status.Data.Required);

        // 向导创建管理员（用户名可选、规范化小写）→ 直接返回会话
        var email = $"setup-{Guid.NewGuid():N}@test.local";
        var setup = (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, SetupPath,
                new SetupRequest { Email = email, Password = "setup-pass-12345678", UserName = "  Founder  " }))
            .Expect(HttpStatusCode.OK);
        Assert.True(setup.Data.User.IsPlatformAdmin);
        Assert.Equal("founder", setup.Data.User.UserName);
        Assert.True(setup.Data.ExpiresAt > DateTimeOffset.UtcNow);

        // 会话端到端可用（平台管理员访问租户端点）
        (await fixture.SendJsonAsync<object>(HttpMethod.Get, $"{ApiRoutes.ManagePrefix}/tenants",
            bearer: setup.Data.Token)).Expect(HttpStatusCode.OK);

        // 完成后：状态翻转 + 再次 setup 409（防借向导再造账号）+ 用户名/大小写变体可登录
        var after = (await fixture.SendJsonAsync<SetupStatusResponse>(HttpMethod.Get, StatusPath))
            .Expect(HttpStatusCode.OK);
        Assert.False(after.Data.Required);
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, SetupPath,
                new SetupRequest { Email = $"other-{Guid.NewGuid():N}@test.local", Password = "setup-pass-12345678" }))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.Conflict);
        (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = "FOUNDER", Password = "setup-pass-12345678" }))
            .Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Setup_WithWeakPassword_Returns400()
    {
        // 校验先于守卫：弱密码直接 400，不消耗初始化
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, SetupPath,
                new SetupRequest { Email = $"weak-{Guid.NewGuid():N}@test.local", Password = "short" }))
            .ExpectError(HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task Setup_WithShortUserName_Returns400()
    {
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, SetupPath,
                new SetupRequest
                {
                    Email = $"name-{Guid.NewGuid():N}@test.local",
                    Password = "setup-pass-12345678",
                    UserName = "ab"
                }))
            .ExpectError(HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }
}
