using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Api.Endpoints.Manage;
using AtomUpdate.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Auth;

/// <summary>首个平台管理员引导（docs/07 §4.2）：用户表为空且 Auth:Bootstrap 已配置时，
/// 幂等创建默认租户 + 平台管理员；配置缺失仅告警跳过，不阻塞启动。</summary>
public static class AuthBootstrapSeeder
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var bootstrap = configuration.GetSection("Auth:Bootstrap");
        var email = bootstrap["Email"];
        var password = bootstrap["Password"];

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>();

        // IgnoreQueryFilters：启动时无租户上下文，租户过滤会恒为空集
        if (await db.Users.IgnoreQueryFilters().AnyAsync())
        {
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AtomUpdate.AuthBootstrap");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            // 未预配管理员：首次部署走 Web 设置向导（POST /api/manage/auth/setup），属预期路径
            logger.LogInformation("用户表为空且未完整配置 Auth:Bootstrap:Email/Password，等待设置向导创建平台管理员");
            return;
        }

        var tenantSlug = bootstrap["TenantSlug"] is { Length: > 0 } slug ? slug : "default";
        var tenant = await EnsureDefaultTenantAsync(db, tenantSlug);

        db.Users.Add(new User
        {
            TenantId = tenant.Id,
            Email = email,
            UserName = AuthEndpoints.NormalizeUserName(bootstrap["UserName"]) ?? "admin",
            PasswordHash = PasswordHasher.Hash(password),
            DisplayName = "平台管理员",
            IsPlatformAdmin = true,
            Status = EntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        logger.LogInformation("已引导创建平台管理员 {Email}（租户 {Slug}）", email, tenantSlug);
    }

    /// <summary>确保默认租户存在（引导种子与设置向导共用；同 slug 已存在则复用）。</summary>
    internal static async Task<Tenant> EnsureDefaultTenantAsync(AtomUpdateDbContext db, string tenantSlug)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == tenantSlug);
        if (tenant is not null)
        {
            return tenant;
        }

        tenant = new Tenant
        {
            Name = "默认租户",
            Slug = tenantSlug,
            Status = EntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Tenants.Add(tenant);
        return tenant;
    }
}
