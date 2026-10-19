namespace AtomUpdate.Api.Auth;

public static class AuthPolicies
{
    /// <summary>管理后台（JWT）：Release 发布与全部管理端点。</summary>
    public const string Manage = "manage";

    /// <summary>平台管理员（JWT + is_platform_admin=true）：租户管理等平台级操作（M2 两级权限模型）。</summary>
    public const string PlatformAdmin = "platform-admin";
}

public static class AuthClaims
{
    public const string TenantId = "tenant_id";
    public const string PlatformAdmin = "is_platform_admin";

    /// <summary>管理后台用户主键；公开端点的匿名主体不含此声明。</summary>
    public const string Subject = "sub";
}

public static class AuthPrincipalExtensions
{
    /// <summary>
    /// 是否管理后台 JWT 会话。不能按方案名判：JwtBearer 给主体写的身份类型是
    /// AuthenticationTypes.Federation 而非 "Bearer"，与方案名不相等。
    /// </summary>
    public static bool IsUserSession(this System.Security.Claims.ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.HasClaim(c => c.Type == AuthClaims.Subject);
}
