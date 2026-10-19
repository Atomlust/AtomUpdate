using System.Net;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Tests.Api;

/// <summary>用户管理端点：创建、列表、更新、删除（物理删除、不可自删）。</summary>
[Collection("api")]
public class ManageUserEndpointTests(ApiFixture fixture)
{
    private const string UsersPath = $"{ApiRoutes.ManagePrefix}/users";

    [Fact]
    public async Task Create_List_Update_Delete_Lifecycle()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var email = $"user{Guid.NewGuid():N}@test.local";

        // 创建
        var created = (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, UsersPath,
            new UserCreateRequest { Email = email, Password = "Password123!", DisplayName = "测试用户" },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.Created);
        Assert.Equal(email, created.Data.Email);

        // 列表能查到
        var list = (await fixture.SendJsonAsync<PagedResult<UserDto>>(HttpMethod.Get, UsersPath,
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.OK);
        Assert.Contains(list.Data.Items, u => u.Email == email);

        // 更新显示名
        var updated = (await fixture.SendJsonAsync<UserDto>(HttpMethod.Patch, $"{UsersPath}/{created.Data.Id}",
            new UserUpdateRequest { DisplayName = "更新名" },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.OK);
        Assert.Equal("更新名", updated.Data.DisplayName);

        // 删除
        (await fixture.SendJsonAsync<object>(HttpMethod.Delete, $"{UsersPath}/{created.Data.Id}",
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.NoContent);

        // 列表不再包含
        var afterDelete = (await fixture.SendJsonAsync<PagedResult<UserDto>>(HttpMethod.Get, UsersPath,
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.OK);
        Assert.DoesNotContain(afterDelete.Data.Items, u => u.Email == email);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        var scenario = await fixture.CreateScenarioAsync();
        (await fixture.SendJsonAsync<object>(HttpMethod.Delete, $"{UsersPath}/999999",
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_Keyword_FiltersByEmailAndDisplayName()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var emailHit = $"kw-{marker}@test.local";
        var nameHit = $"关键词{marker}";
        var emailMiss = $"other-{marker}@test.local";
        (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, UsersPath,
            new UserCreateRequest { Email = emailHit, Password = "Password123!" },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.Created);
        (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, UsersPath,
            new UserCreateRequest { Email = $"name-{marker}@test.local", Password = "Password123!", DisplayName = nameHit },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.Created);
        (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, UsersPath,
            new UserCreateRequest { Email = emailMiss, Password = "Password123!" },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.Created);

        // 按邮箱独有片段搜索：仅命中 kw- 用户
        var byEmail = (await fixture.SendJsonAsync<PagedResult<UserDto>>(HttpMethod.Get, $"{UsersPath}?keyword=kw-{marker}",
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.OK);
        Assert.Equal(1, byEmail.Data.Total);
        Assert.Contains(byEmail.Data.Items, u => u.Email == emailHit);

        // 按显示名搜索：命中显示名用户
        var byName = (await fixture.SendJsonAsync<PagedResult<UserDto>>(HttpMethod.Get,
            $"{UsersPath}?keyword={Uri.EscapeDataString(nameHit)}",
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.OK);
        Assert.Equal(1, byName.Data.Total);
        Assert.Contains(byName.Data.Items, u => u.Email == $"name-{marker}@test.local");

        // 无关关键字：0 结果
        var none = (await fixture.SendJsonAsync<PagedResult<UserDto>>(HttpMethod.Get,
            $"{UsersPath}?keyword={Uri.EscapeDataString("不存在的关键字xyz")}",
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.OK);
        Assert.Equal(0, none.Data.Total);
    }

    [Fact]
    public async Task UserName_Login_Update_Duplicate_And_Clear()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var email = $"uname{Guid.NewGuid():N}@test.local";
        var loginPath = "/api/manage/auth/login";

        // 创建带用户名的用户
        var created = (await fixture.SendJsonAsync<UserDto>(HttpMethod.Post, UsersPath,
            new UserCreateRequest { Email = email, Password = "Password123!", UserName = "  LoginGuy  " },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.Created);
        Assert.Equal("loginguy", created.Data.UserName);

        // 用户名登录（大小写不敏感，输入多余空格容忍）+ DTO 返回 UserName
        var byUserName = (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, loginPath,
            new LoginRequest { Email = "  LoginGuy  ", Password = "Password123!" }))
            .Expect(HttpStatusCode.OK);
        Assert.Equal("loginguy", byUserName.Data.User.UserName);

        // 邮箱仍可登录
        (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, loginPath,
            new LoginRequest { Email = email, Password = "Password123!" })).Expect(HttpStatusCode.OK);

        // 重名创建 → 409（含大小写变体）
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, UsersPath,
            new UserCreateRequest { Email = $"dup{Guid.NewGuid():N}@test.local", Password = "Password123!", UserName = "LOGINguy" },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.Conflict);

        // 更新为新用户名 → 旧用户名失效、新用户名可登录
        var renamed = (await fixture.SendJsonAsync<UserDto>(HttpMethod.Patch, $"{UsersPath}/{created.Data.Id}",
            new UserUpdateRequest { UserName = "loginguy2" },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.OK);
        Assert.Equal("loginguy2", renamed.Data.UserName);
        (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, loginPath,
            new LoginRequest { Email = "loginguy", Password = "Password123!" }))
            .Expect(HttpStatusCode.Unauthorized);
        (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, loginPath,
            new LoginRequest { Email = "loginguy2", Password = "Password123!" }))
            .Expect(HttpStatusCode.OK);

        // 清空用户名（空串）→ 仅邮箱可登录
        var cleared = (await fixture.SendJsonAsync<UserDto>(HttpMethod.Patch, $"{UsersPath}/{created.Data.Id}",
            new UserUpdateRequest { UserName = "" },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.OK);
        Assert.Null(cleared.Data.UserName);
        (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, loginPath,
            new LoginRequest { Email = "loginguy2", Password = "Password123!" }))
            .Expect(HttpStatusCode.Unauthorized);
        (await fixture.SendJsonAsync<LoginResponse>(HttpMethod.Post, loginPath,
            new LoginRequest { Email = email, Password = "Password123!" }))
            .Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserName_TooShort_Returns400()
    {
        var scenario = await fixture.CreateScenarioAsync();
        (await fixture.SendJsonAsync<object>(HttpMethod.Post, UsersPath,
            new UserCreateRequest { Email = $"short{Guid.NewGuid():N}@test.local", Password = "Password123!", UserName = "ab" },
            bearer: scenario.AdminToken)).Expect(HttpStatusCode.BadRequest);
    }
}
