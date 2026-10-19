using System.Globalization;
using AtomUpdate.Api.Data;

namespace AtomUpdate.Api.Auth;

/// <summary>将认证主体中的 tenant_id claim 同步到请求级 TenantContext（JWT 路径）。</summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            context.User.FindFirst(AuthClaims.TenantId)?.Value is { } rawTenantId &&
            long.TryParse(rawTenantId, CultureInfo.InvariantCulture, out var tenantId))
        {
            tenantContext.TenantId = tenantId;
        }

        await next(context);
    }
}
