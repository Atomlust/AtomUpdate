using System.Text;
using AtomUpdate.Core.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace AtomUpdate.Api.Auth;

public static class AuthSetup
{
    /// <summary>JWT 装配。签名密钥：显式 Auth:Jwt:SigningKey 优先，缺失时自动生成并持久化（JwtSigningKeyProvider）；
    /// contentRootPath 供密钥文件相对路径解析（零配置部署，docs/09）。</summary>
    public static IServiceCollection AddAtomUpdateAuth(this IServiceCollection services, IConfiguration configuration,
        string? contentRootPath = null)
    {
        var jwt = configuration.GetSection("Auth:Jwt");
        var signingKey = JwtSigningKeyProvider.Resolve(configuration, contentRootPath ?? AppContext.BaseDirectory);
        services.AddSingleton(signingKey);

        var issuer = jwt["Issuer"] ?? throw new InvalidOperationException("缺少配置 Auth:Jwt:Issuer");
        var audience = jwt["Audience"] ?? throw new InvalidOperationException("缺少配置 Auth:Jwt:Audience");

        services.AddAuthentication(options => options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey.Key)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>().CreateLogger("AtomUpdate.Auth");
                        // 诊断：401 时记录路径与是否携带 Bearer（含验签失败原因，JwtBearer 自身 Debug 级别）
                        var hasAuth = context.Request.Headers.ContainsKey("Authorization");
                        logger.LogWarning(
                            "JWT challenge 401: {Method} {Path}, AuthorizationHeader={HasAuth}, Error={Error}, Description={Description}",
                            context.Request.Method, context.Request.Path, hasAuth,
                            context.Error, context.ErrorDescription);
                        context.HandleResponse();
                        await AuthResponses.WriteErrorAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            ErrorCodes.Unauthorized, "缺少或无效的访问令牌");
                    },
                    OnForbidden = context => AuthResponses.WriteErrorAsync(context.HttpContext,
                        StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "访问令牌权限不足")
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Manage, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.PlatformAdmin, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AuthClaims.PlatformAdmin, "true"));

        return services;
    }
}
