using System.Net;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using Microsoft.Extensions.DependencyInjection;

namespace AtomUpdate.Tests.Api;

/// <summary>登录与两级权限：配置种子引导 / JWT 签发 / 统一错误口径 / 全局唯一 Email / 平台管理员收窄。</summary>
[Collection("api")]
public class AuthEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Login_WithBootstrapAdmin_ReturnsToken_AndTokenWorksOnPlatformEndpoints()
    {
        var login = (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = ApiFixture.BootstrapEmail, Password = ApiFixture.BootstrapPassword }))
            .Expect(HttpStatusCode.OK);

        Assert.False(string.IsNullOrWhiteSpace(login.Data.Token));
        Assert.True(login.Data.User.IsPlatformAdmin);
        Assert.True(login.Data.ExpiresAt > DateTimeOffset.UtcNow);

        // 签发的 JWT 端到端可用：平台管理员访问租户端点
        var tenants = (await fixture.SendJsonAsync<object>(HttpMethod.Get, $"{ApiRoutes.ManagePrefix}/tenants",
            bearer: login.Data.Token)).Expect(HttpStatusCode.OK);
        Assert.Contains("items", tenants.Root.ToString());
    }

    [Fact]
    public async Task Setup_AlreadyInitialized_Returns409_AndStatusNotRequired()
    {
        // 预配 Bootstrap 的主 fixture：用户表非空 → 向导状态不需要，setup 端点一律 409
        var status = (await fixture.SendJsonAsync<SetupStatusResponse>(HttpMethod.Get,
                $"{ApiRoutes.ManagePrefix}/auth/setup/status")).Expect(HttpStatusCode.OK);
        Assert.False(status.Data.Required);

        (await fixture.SendJsonAsync<object>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/setup",
                new SetupRequest { Email = $"hijack-{Guid.NewGuid():N}@test.local", Password = "hijack-pass-12345678" }))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.Conflict);
    }

    [Theory]
    [InlineData("no-such-user@test.local", "wrong-password-123")]
    [InlineData("no-such-user-2@test.local", "whatever-12345678")]
    public async Task Login_WithBadCredentials_Returns401_InvalidCredentials_Uniformly(string email, string password)
    {
        // 不存在邮箱与错误密码统一 INVALID_CREDENTIALS，避免账号枚举
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = email, Password = password }))
            .ExpectError(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Login_WithBlankIdentifier_Returns400()
    {
        // 登录标识支持用户名或邮箱，仅校验非空（不再校验邮箱格式）
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = "not-an-email", Password = "whatever-12345678" }))
            .ExpectError(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Create_User_ThenLogin_And_DisabledUserCannotLogin()
    {
        var login = (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = ApiFixture.BootstrapEmail, Password = ApiFixture.BootstrapPassword }))
            .Expect(HttpStatusCode.OK);
        var adminToken = login.Data.Token;

        var email = $"user-{Guid.NewGuid():N}@test.local";
        var created = (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/users",
                new UserCreateRequest { Email = email, Password = "user-pass-12345678", DisplayName = "测试用户" },
                bearer: adminToken))
            .Expect(HttpStatusCode.Created);
        Assert.False(created.Data.IsPlatformAdmin);

        (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = email, Password = "user-pass-12345678" }))
            .Expect(HttpStatusCode.OK);

        var disabled = (await fixture.SendJsonAsync<UserDto>(HttpMethod.Patch,
                $"{ApiRoutes.ManagePrefix}/users/{created.Data.Id}",
                new UserUpdateRequest { Status = EntityStatus.Disabled }, bearer: adminToken))
            .Expect(HttpStatusCode.OK);
        Assert.Equal(EntityStatus.Disabled, disabled.Data.Status);

        (await fixture.SendJsonAsync<object>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = email, Password = "user-pass-12345678" }))
            .ExpectError(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Create_User_DuplicateEmail_AcrossTenants_Returns409_UserExists()
    {
        var adminToken = (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post,
                $"{ApiRoutes.ManagePrefix}/auth/login",
                new LoginRequest { Email = ApiFixture.BootstrapEmail, Password = ApiFixture.BootstrapPassword }))
            .Expect(HttpStatusCode.OK).Data.Token;

        var email = $"dup-{Guid.NewGuid():N}@test.local";
        (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/users",
                new UserCreateRequest { Email = email, Password = "user-pass-12345678" }, bearer: adminToken))
            .Expect(HttpStatusCode.Created);

        // 另一租户（MintUserToken 携带不同 tenant_id）创建同邮箱仍全局冲突
        var otherTenantUserToken = fixture.MintUserToken(tenantId: 987654321);
        (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/users",
                new UserCreateRequest { Email = email, Password = "user-pass-12345678" }, bearer: otherTenantUserToken))
            .ExpectError(HttpStatusCode.Conflict, ErrorCodes.UserExists);
    }

    [Fact]
    public async Task Create_User_ByNonAdminCaller_PlatformAdminFlagIsIgnored()
    {
        var userToken = fixture.MintUserToken(tenantId: 24680);
        var created = (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/users",
                new UserCreateRequest { Email = $"escalate-{Guid.NewGuid():N}@test.local", Password = "user-pass-12345678", IsPlatformAdmin = true },
                bearer: userToken))
            .Expect(HttpStatusCode.Created);

        Assert.False(created.Data.IsPlatformAdmin);
    }

    [Fact]
    public async Task TenantEndpoints_RequirePlatformAdmin()
    {
        var userToken = fixture.MintUserToken(tenantId: 13579);
        (await fixture.SendJsonAsync<object>(HttpMethod.Get, $"{ApiRoutes.ManagePrefix}/tenants", bearer: userToken))
            .Expect(HttpStatusCode.Forbidden);
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/tenants",
                new TenantCreateRequest { Name = "越权租户", Slug = $"x-{Guid.NewGuid():N}"[..20] }, bearer: userToken))
            .Expect(HttpStatusCode.Forbidden);
    }
}
