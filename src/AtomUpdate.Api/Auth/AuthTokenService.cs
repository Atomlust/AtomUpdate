using System.Globalization;
using System.Text;
using AtomUpdate.Api.Data.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AtomUpdate.Api.Auth;

/// <summary>管理后台用户 JWT 签发（HS256，与 AddAtomUpdateAuth 验签参数同源）。
/// 签名密钥取自启动期解析的 JwtSigningKeyInfo（显式配置或自动生成密钥文件，docs/09）。</summary>
public sealed class AuthTokenService(IConfiguration configuration, JwtSigningKeyInfo signingKey)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var jwt = configuration.GetSection("Auth:Jwt");
        var issuer = jwt["Issuer"] ?? throw new InvalidOperationException("缺少配置 Auth:Jwt:Issuer");
        var audience = jwt["Audience"] ?? throw new InvalidOperationException("缺少配置 Auth:Jwt:Audience");
        var lifetimeMinutes = configuration.GetValue("Auth:Jwt:LifetimeMinutes", 480);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(lifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey.Key)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(CultureInfo.InvariantCulture),
                ["email"] = user.Email,
                [AuthClaims.TenantId] = user.TenantId.ToString(CultureInfo.InvariantCulture),
                [AuthClaims.PlatformAdmin] = user.IsPlatformAdmin ? "true" : "false"
            }
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
