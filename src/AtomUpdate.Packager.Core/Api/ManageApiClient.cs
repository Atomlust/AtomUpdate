using System.Text;
using System.Text.Json;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Serialization;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Packager.Core.Api;

/// <summary>管理后台登录/浏览客户端：账号密码换 JWT → 拉项目/应用列表（GUI 下拉选择，免手输数字 ID）。
/// 与 HttpAtomUpdateApiClient（登录会话 JWT 发布链路）分离：登录职责不同。</summary>
public sealed class ManageApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = AtomUpdateJson.Create();

    private readonly HttpClient _http;
    private string? _token;

    public ManageApiClient(string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "缺少服务端地址");
        }

        _http = new HttpClient
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("atomupdate-packager/1.0");
    }

    public bool IsLoggedIn => _token is not null;

    /// <summary>登录会话令牌（发布链路复用：Ingest 策略对 JWT 会话直通，免建 Ingest Key）。</summary>
    public string? Token => _token;

    /// <summary>登录并保存认证令牌，返回登录用户邮箱。</summary>
    public async Task<string> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "账号与密码不能为空");
        }

        var (status, body) = await SendAsync(HttpMethod.Post, "/api/manage/auth/login",
            new { email, password }, cancellationToken);
        if (status == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "登录失败：账号或密码错误");
        }

        EnsureSuccess(status, body, "登录失败");
        _token = Deserialize<LoginResponse>(body, "登录响应解析失败")?.Token
            ?? throw new PackagerException(PackagerErrorCodes.ApiFailed, "登录响应缺少令牌");
        return Deserialize<UserDto>(ExtractUser(body), "用户信息解析失败")?.Email ?? email;
    }

    public async Task<List<ProjectDto>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetPagedAsync<PagedResult<ProjectDto>>("/api/manage/projects?page=1&pageSize=200",
            "拉取项目列表失败", cancellationToken);
        return page?.Items?.ToList() ?? [];
    }

    public async Task<List<AppDto>> ListAppsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var page = await GetPagedAsync<PagedResult<AppDto>>(
            $"/api/manage/projects/{projectId}/apps?page=1&pageSize=200", "拉取应用列表失败", cancellationToken);
        return page?.Items?.ToList() ?? [];
    }

    /// <summary>查询应用最新一条版本记录（管理端 releases 列表第一页第一条），无记录返回 null。</summary>
    public async Task<ReleaseDto?> ListLatestReleaseAsync(long projectId, long appId,
        CancellationToken cancellationToken = default)
    {
        var page = await GetPagedAsync<PagedResult<ReleaseDto>>(
            $"/api/projects/{projectId}/apps/{appId}/releases?page=1&pageSize=1", "拉取最新版本失败", cancellationToken);
        return page?.Items is { Count: > 0 } items ? items[0] : null;
    }

    private async Task<T?> GetPagedAsync<T>(string url, string errorTitle, CancellationToken ct)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, url, payload: null, ct);
        EnsureSuccess(status, body, errorTitle);
        return Deserialize<T>(body, $"{errorTitle}：响应解析失败");
    }

    private static string ExtractUser(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("user", out var user) ? user.GetRawText() : "null";
    }

    private async Task<(System.Net.HttpStatusCode Status, string Body)> SendAsync(HttpMethod method,
        string url, object? payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (_token is not null)
        {
            request.Headers.Authorization = new("Bearer", _token);
        }

        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    private static void EnsureSuccess(System.Net.HttpStatusCode status, string body, string errorTitle)
    {
        if ((int)status is >= 200 and < 300)
        {
            return;
        }

        ErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            // 非 JSON 错误体，走通用提示
        }

        var detail = error is null ? $"HTTP {(int)status}" : $"{error.Code}：{error.Message}";
        throw new PackagerException(PackagerErrorCodes.ApiFailed, $"{errorTitle}（{detail}）");
    }

    private static T? Deserialize<T>(string body, string errorTitle)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, errorTitle, exception);
        }
    }

    public void Dispose() => _http.Dispose();
}
