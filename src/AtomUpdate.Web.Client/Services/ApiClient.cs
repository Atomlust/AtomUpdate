using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Core.Serialization;
using Microsoft.AspNetCore.Components;

namespace AtomUpdate.Web.Client.Services;

public sealed record ApiResult<T>(HttpStatusCode Status, T? Data, string? ErrorCode, string? ErrorMessage)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;
}

/// <summary>管理后台 API 客户端：自动携带 JWT；401（过期/失效）时清除登录态并跳回登录页。
/// 不用 DelegatingHandler 附 token：IHttpClientFactory 会缓存处理管道并复用首个 scope 的
/// AuthState，Blazor Server（Auto 回退）下拿不到当前电路的登录态；ApiClient 是 scoped，
/// 天然持有正确的 AuthState。</summary>
public sealed class ApiClient(IHttpClientFactory factory, AuthState auth, NavigationManager navigation)
{
    public const string HttpClientName = "AtomUpdate.Admin";

    public async Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request)
    {
        using var response = await Client().PostAsJsonAsync($"{ApiRoutes.ManagePrefix}/auth/login", request, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response);
            return new(response.StatusCode, null, error?.Code, error?.Message);
        }

        var data = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        if (data is not null)
        {
            await auth.SignInAsync(data.Token);
        }

        return new(response.StatusCode, data, null, null);
    }

    /// <summary>首次部署向导状态（公开端点）：true = 用户表为空，需先完成管理员初始化。</summary>
    public async Task<ApiResult<SetupStatusResponse>> GetSetupStatusAsync()
    {
        using var response = await Client().GetAsync($"{ApiRoutes.ManagePrefix}/auth/setup/status");
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response);
            return new(response.StatusCode, null, error?.Code, error?.Message);
        }

        var data = await response.Content.ReadFromJsonAsync<SetupStatusResponse>(JsonOptions);
        return new(response.StatusCode, data, null, null);
    }

    /// <summary>设置向导提交：创建平台管理员并直接建立登录会话；已初始化返回 409。</summary>
    public async Task<ApiResult<LoginResponse>> SetupAsync(SetupRequest request)
    {
        using var response = await Client().PostAsJsonAsync($"{ApiRoutes.ManagePrefix}/auth/setup", request, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response);
            return new(response.StatusCode, null, error?.Code, error?.Message);
        }

        var data = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        if (data is not null)
        {
            await auth.SignInAsync(data.Token);
        }

        return new(response.StatusCode, data, null, null);
    }

    public async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (auth.Token is { } token)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        if (body is HttpContent httpContent)
        {
            request.Content = httpContent;
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        using var response = await Client().SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // token 过期/失效：回登录页重新建立会话
            await auth.SignOutAsync();
            navigation.NavigateTo("login", replace: true);
            return new(response.StatusCode, default, ErrorCodes.InvalidCredentials, "登录已失效，请重新登录");
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response);
            return new(response.StatusCode, default, error?.Code, error?.Message);
        }

        if (typeof(T) == typeof(object))
        {
            return new(response.StatusCode, default, null, null);
        }

        var data = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return new(response.StatusCode, data, null, null);
    }

    public Task<ApiResult<T>> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path);

    private HttpClient Client() => factory.CreateClient(HttpClientName);

    private static async Task<ErrorResponse?> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = AtomUpdateJson.Create();
}

public static class AdminClientServiceCollectionExtensions
{
    /// <summary>登录态 + API 客户端。baseAddress 由宿主提供（服务端为回环地址，WASM 为当前源）。</summary>
    public static IServiceCollection AddAdminClientServices(this IServiceCollection services, string? baseAddress = null)
    {
        services.AddScoped<AuthState>();
        services.AddHttpClient(ApiClient.HttpClientName, client =>
            {
                if (baseAddress is not null)
                {
                    client.BaseAddress = new Uri(baseAddress);
                }
            });
        services.AddScoped<ApiClient>();
        return services;
    }
}
