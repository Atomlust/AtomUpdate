using System.Text.Json;
using Microsoft.JSInterop;

namespace AtomUpdate.Web.Client.Services;

/// <summary>WASM 侧登录态：JWT 存 localStorage，claims 从 token 载荷解析（预渲染阶段无 JS，标记未就绪即可）。</summary>
public sealed class AuthState(IJSRuntime js)
{
    private const string StorageKey = "atomupdate.admin.token";
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public bool IsReady { get; private set; }
    public string? Token { get; private set; }
    public string? Email { get; private set; }
    public long? UserId { get; private set; }
    public long? TenantId { get; private set; }
    public bool IsPlatformAdmin { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool IsAuthenticated => Token is not null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);

    /// <summary>登录态就绪/变化后通知布局重渲染（权限点菜单随之显隐）。</summary>
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (IsReady)
        {
            return;
        }

        string? token = null;
        try
        {
            token = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
            // 预渲染阶段无 JS 运行时，WASM 交互后由 OnAfterRenderAsync 再次初始化
            return;
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            Apply(token);
        }

        IsReady = true;
        Changed?.Invoke();
    }

    public async Task SignInAsync(string token)
    {
        Apply(token);
        IsReady = true;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, token);
        }
        catch (JSException)
        {
        }
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        Token = null;
        Email = null;
        UserId = null;
        TenantId = null;
        IsPlatformAdmin = false;
        ExpiresAt = null;
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch (JSException)
        {
        }
        Changed?.Invoke();
    }

    private void Apply(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(DecodeBase64Url(parts[1]), PayloadJson);
            if (payload is null)
            {
                return;
            }

            Token = token;
            Email = payload.TryGetValue("email", out var email) && email.ValueKind == JsonValueKind.String ? email.GetString() : null;
            if (payload.TryGetValue("sub", out var sub) && sub.ValueKind == JsonValueKind.String && long.TryParse(sub.GetString(), out var userId))
            {
                UserId = userId;
            }
            IsPlatformAdmin = payload.TryGetValue("is_platform_admin", out var admin) && admin.ValueKind == JsonValueKind.String && admin.GetString() == "true";
            if (payload.TryGetValue("tenant_id", out var tenant) && tenant.ValueKind == JsonValueKind.String && long.TryParse(tenant.GetString(), out var tenantId))
            {
                TenantId = tenantId;
            }
            if (payload.TryGetValue("exp", out var exp) && exp.ValueKind == JsonValueKind.Number)
            {
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
            }
        }
        catch (FormatException)
        {
            // token 载荷损坏按未登录处理
        }
        catch (JsonException)
        {
        }
    }

    private static string DecodeBase64Url(string segment) =>
        Convert.FromBase64String(segment.Length % 4 == 0 ? segment : segment + new string('=', 4 - segment.Length % 4)) is { Length: > 0 } bytes
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : throw new FormatException();
}
