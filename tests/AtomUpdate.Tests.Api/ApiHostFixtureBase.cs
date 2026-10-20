using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtomUpdate.Api.Data;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Core.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtomUpdate.Tests.Api;

/// <summary>双 provider 共用的集成测试宿主基类（docs/11 §5 测试矩阵）：HTTP/JWT/Manage/Release 编排辅助，
/// 与数据库 provider 无关；宿主装配（环境变量注入 + 迁移）由各 provider fixture 自行完成。</summary>
public abstract class ApiHostFixtureBase : WebApplicationFactory<Program>
{
    /// <summary>引导平台管理员（宿主启动时经 AuthBootstrapSeeder 种子化），登录测试直接使用。</summary>
    public const string BootstrapEmail = "admin@test.local";
    public const string BootstrapPassword = "test-admin-12345678";

    protected const string SigningKey = "test-only-signing-key-change-me-0123456789";
    protected const string Issuer = "AtomUpdate";
    protected const string Audience = "AtomUpdate";

    public static readonly JsonSerializerOptions Json = AtomUpdateJson.Create();

    private HttpClient? _client;

    public HttpClient Client => _client ??= CreateClient();

    /// <summary>立即物化宿主：注册期配置读取走进程环境变量（见各 fixture InitializeAsync），创建后即固定。</summary>
    protected HttpClient CreateClientNow()
    {
        _client ??= CreateClient();
        return _client;
    }

    // ---------- JWT（HS256，与 AuthSetup 验签参数一致） ----------

    public string MintToken(long tenantId, string subject = "integration-tests", bool platformAdmin = true)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = subject,
            ["tenant_id"] = tenantId.ToString(CultureInfo.InvariantCulture),
            ["is_platform_admin"] = platformAdmin ? "true" : "false",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds()
        }));
        var signature = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(SigningKey),
            Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    /// <summary>非平台管理员令牌（两级权限模型下的普通租户用户）。</summary>
    public string MintUserToken(long tenantId, string subject = "tenant-user") =>
        MintToken(tenantId, subject, platformAdmin: false);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---------- HTTP ----------

    public async Task<ApiResult<T>> SendJsonAsync<T>(HttpMethod method, string path, object? body = null,
        string? bearer = null, Action<HttpRequestMessage>? configure = null,
        HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        configure?.Invoke(request);
        using var response = await (client ?? Client).SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new ApiResult<T>(response.StatusCode, text, response.Headers.ETag?.Tag);
    }

    public Task<ApiResult<CheckResponse>> CheckAsync(CheckRequest body,
        string? ifNoneMatch = null, HttpClient? client = null) =>
        SendJsonAsync<CheckResponse>(HttpMethod.Post, ApiRoutes.Check, body,
            configure: ifNoneMatch is null
                ? null
                : request => request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, ifNoneMatch),
            client: client);

    // ---------- 请求体构造 ----------

    public static CheckRequest CheckBody(Scenario scenario, string currentVersion, UpdateChannel? channel = null,
        int policyVersion = 0, UpdatePlatform? platform = null,
        UpdateArch arch = UpdateArch.X64, string? installationId = null, string? projectKey = null,
        string? appId = null) => new()
    {
        ProjectKey = projectKey ?? scenario.ProjectKey,
        AppId = appId ?? scenario.AppBusinessId,
        CurrentVersion = currentVersion,
        // 平台默认跟随应用注册值；显式传入不同值即为 /api/check 的 PLATFORM_MISMATCH 场景
        Platform = platform ?? scenario.Platform,
        Arch = arch,
        Channel = channel,
        InstallationId = installationId ?? Guid.NewGuid().ToString("D"),
        PolicyVersion = policyVersion
    };

    public static ReleaseAssetInput FullAsset(string url, string sha256, PackageType packageType = PackageType.Zip,
        UpdateArch arch = UpdateArch.X64) => new()
    {
        Kind = AssetKind.Full,
        Arch = arch,
        PackageType = packageType,
        Url = url,
        Sha256 = sha256
    };

    public static ReleaseAssetInput DeltaAsset(string fromVersion, string url, string sha256,
        PackageType packageType = PackageType.Zip, UpdateArch arch = UpdateArch.X64) => new()
    {
        Kind = AssetKind.Delta,
        FromVersion = fromVersion,
        Arch = arch,
        PackageType = packageType,
        Url = url,
        Sha256 = sha256
    };

    /// <summary>版本请求：平台属于应用，架构在每个资产上，这里只给版本号与包。</summary>
    public static ReleaseCreateRequest WindowsRelease(string version, params ReleaseAssetInput[] assets) => new()
    {
        Version = version,
        Assets = assets
    };

    public static string Sha(char hexDigit) => new(hexDigit, 64);

    // ---------- Manage 编排（真实端点） ----------

    public async Task<TenantDto> CreateTenantAsync(string slug)
    {
        var result = await SendJsonAsync<TenantDto>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/tenants",
            new TenantCreateRequest { Name = slug, Slug = slug }, bearer: MintToken(0));
        return result.Expect(HttpStatusCode.Created).Data;
    }

    public async Task<ProjectDto> CreateProjectAsync(string token, string key)
    {
        var result = await SendJsonAsync<ProjectDto>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/projects",
            new ProjectCreateRequest { Key = key, Name = key }, bearer: token);
        return result.Expect(HttpStatusCode.Created).Data;
    }

    /// <summary>平台是应用的属性：一个 appId 只服务一个平台，应用类型跟随平台取值。</summary>
    public async Task<AppDto> CreateAppAsync(string token, long projectId, string appId,
        UpdatePlatform platform = UpdatePlatform.Windows)
    {
        var result = await SendJsonAsync<AppDto>(HttpMethod.Post, $"{ApiRoutes.ManagePrefix}/projects/{projectId}/apps",
            new AppCreateRequest
            {
                AppId = appId,
                Name = appId,
                Platform = platform,
                Type = AppType.Desktop
            }, bearer: token);
        return result.Expect(HttpStatusCode.Created).Data;
    }

    public async Task<PolicyDto> UpdatePolicyAsync(string token, long appId, PolicyUpdateRequest request)
    {
        var result = await SendJsonAsync<PolicyDto>(HttpMethod.Patch,
            $"{ApiRoutes.ManagePrefix}/apps/{appId}/policy", request, bearer: token);
        return result.Expect(HttpStatusCode.OK).Data;
    }

    /// <summary>租户 + 项目 + 应用（预置策略 v1）。通道为枚举预设，缺省 stable。</summary>
    public async Task<Scenario> CreateScenarioAsync(bool allowForceUpdate = false, bool allowDowngrade = false,
        string? projectKey = null, UpdatePlatform platform = UpdatePlatform.Windows)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = await CreateTenantAsync($"t{suffix}");
        var adminToken = MintToken(tenant.Id);
        var project = await CreateProjectAsync(adminToken, projectKey ?? $"p{suffix}");
        var app = await CreateAppAsync(adminToken, project.Id, $"com.{suffix}.app", platform);
        if (allowForceUpdate || allowDowngrade)
        {
            await UpdatePolicyAsync(adminToken, app.Id, new PolicyUpdateRequest
            {
                AllowForceUpdate = allowForceUpdate,
                AllowDowngrade = allowDowngrade
            });
        }

        return new Scenario(tenant.Id, project.Id, project.Key, app.Id, app.AppId, app.Platform, adminToken);
    }

    // ---------- Release 编排 ----------

    public static string ReleasePath(long projectId, long appId) =>
        $"/api/projects/{projectId}/apps/{appId}/releases";

    public Task<ApiResult<ReleaseDto>> TryCreateReleaseAsync(string bearer, long projectId, long appId,
        ReleaseCreateRequest request) =>
        SendJsonAsync<ReleaseDto>(HttpMethod.Post, ReleasePath(projectId, appId), request, bearer: bearer);

    public async Task<ReleaseDto> CreateReleaseAsync(string bearer, long projectId, long appId,
        ReleaseCreateRequest request) =>
        (await TryCreateReleaseAsync(bearer, projectId, appId, request)).Expect(HttpStatusCode.Created).Data;

    public Task<ApiResult<ReleaseDto>> PublishAsync(string bearer, long projectId, long appId, long releaseId,
        UpdateChannel channel) =>
        SendJsonAsync<ReleaseDto>(HttpMethod.Post, $"{ReleasePath(projectId, appId)}/{releaseId}/publish",
            new ReleasePublishRequest { Channels = [channel] }, bearer: bearer);

    public Task<ApiResult<ReleaseDto>> UnpublishAsync(string bearer, long projectId, long appId, long releaseId) =>
        SendJsonAsync<ReleaseDto>(HttpMethod.Post, $"{ReleasePath(projectId, appId)}/{releaseId}/unpublish",
            body: null, bearer: bearer);

    public async Task<ReleaseDto> CreateAndPublishAsync(string bearer, Scenario scenario, ReleaseCreateRequest request,
        UpdateChannel? channel = null)
    {
        var release = await CreateReleaseAsync(bearer, scenario.ProjectId, scenario.AppId, request);
        return (await PublishAsync(bearer, scenario.ProjectId, scenario.AppId, release.Id,
            channel ?? UpdateChannel.Stable)).Expect(HttpStatusCode.OK).Data;
    }

    // ---------- 数据库断言 ----------

    public async Task<T> QueryAsync<T>(long tenantId, Func<AtomUpdateDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
        return await query(scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>());
    }

    public async Task WaitForDbAsync(long tenantId, Func<AtomUpdateDbContext, Task<bool>> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!await QueryAsync(tenantId, predicate))
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("等待数据库异步写入超时");
            }

            await Task.Delay(100);
        }
    }
}

public sealed record Scenario(
    long TenantId,
    long ProjectId,
    string ProjectKey,
    long AppId,
    string AppBusinessId,
    UpdatePlatform Platform,
    string AdminToken);

public sealed record ApiResult<T>(HttpStatusCode Status, string Body, string? ETag = null)
{
    public T Data => JsonSerializer.Deserialize<T>(Body, ApiHostFixtureBase.Json)!;

    public ErrorResponse Error => JsonSerializer.Deserialize<ErrorResponse>(Body, ApiHostFixtureBase.Json)!;

    public JsonElement Root
    {
        get
        {
            using var document = JsonDocument.Parse(Body);
            return document.RootElement.Clone();
        }
    }

    public ApiResult<T> Expect(HttpStatusCode expected)
    {
        Assert.True(expected == Status, $"期望 HTTP {(int)expected}，实际 {(int)Status}；响应: {Body}");
        return this;
    }

    public ApiResult<T> ExpectError(HttpStatusCode expected, string code)
    {
        Expect(expected);
        Assert.Equal(code, Error.Code);
        return this;
    }

    public ApiResult<T> ExpectNoField(string name)
    {
        Assert.False(Root.TryGetProperty(name, out _), $"响应不应包含字段 {name}：{Body}");
        return this;
    }
}
