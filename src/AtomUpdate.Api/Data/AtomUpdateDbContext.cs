using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Core.Enums;
using IdGen;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace AtomUpdate.Api.Data;

/// <summary>应用主上下文：npgsql 模式直接使用；sqlite 模式复用同一模型（provider 专有建模按 ProviderName 分支）。</summary>
public class AtomUpdateDbContext : DbContext
{
    private readonly TenantContext _tenantContext;
    private readonly IIdGenerator<long> _idGenerator;

    /// <summary>DI 激活入口：EF 要求每上下文类型以各自的 DbContextOptions&lt;TContext&gt; 构造
    /// （同进程注册多个上下文时，非泛型选项服务的转发目标不保证对应本类型）。</summary>
    public AtomUpdateDbContext(DbContextOptions<AtomUpdateDbContext> options, TenantContext tenantContext,
        IIdGenerator<long> idGenerator)
        : this((DbContextOptions)options, tenantContext, idGenerator)
    {
    }

    /// <summary>非泛型选项通道：供派生上下文 SqliteAtomUpdateDbContext（docs/11 §3.2 迁移隔离）与设计时工厂构造；
    /// DbContext 基类校验 options.ContextType 与实例类型一致，两分支均满足。</summary>
    protected AtomUpdateDbContext(DbContextOptions options, TenantContext tenantContext, IIdGenerator<long> idGenerator)
        : base(options)
    {
        _tenantContext = tenantContext;
        _idGenerator = idGenerator;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<App> Apps => Set<App>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UpdatePolicy> UpdatePolicies => Set<UpdatePolicy>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<ReleaseChannel> ReleaseChannels => Set<ReleaseChannel>();
    public DbSet<ReleaseAsset> ReleaseAssets => Set<ReleaseAsset>();
    public DbSet<ReleaseNote> ReleaseNotes => Set<ReleaseNote>();
    public DbSet<PackageSource> PackageSources => Set<PackageSource>();
    public DbSet<UpdateCheckLog> UpdateCheckLogs => Set<UpdateCheckLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<DownloadLog> DownloadLogs => Set<DownloadLog>();
    public DbSet<InstallationReport> InstallationReports => Set<InstallationReport>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // provider 专有建模（docs/11 §3.3）：SQLite 无 text[]/jsonb/NULLS NOT DISTINCT/IDENTITY 约定；
        // Npgsql 分支必须与既有迁移快照逐字节一致（否则会误生成 pg 差异迁移）
        var isSqlite = Database.IsSqlite();

        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<App>(e =>
        {
            e.HasIndex(x => new { x.ProjectId, x.AppId }).IsUnique();
            e.Property(x => x.AppId).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Platform).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<User>(e =>
        {
            // Email 全局唯一（跨租户），登录仅"邮箱+密码"
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(200);
            // UserName 可选登录名：全局唯一，NULL 不参与唯一约束（部分索引；列名带引号 PascalCase，pg/SQLite 语法一致）
            e.HasIndex(x => x.UserName).IsUnique().HasFilter("\"UserName\" IS NOT NULL");
            e.Property(x => x.UserName).HasMaxLength(64);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<UpdatePolicy>(e =>
        {
            // 幂等去重唯一索引：pg NULLS NOT DISTINCT；SQLite 唯一索引恒 NULLS DISTINCT，应用层补偿（docs/11 §3.3）
            var uniqueIndex = e.HasIndex(x => new { x.AppId, x.Channel }).IsUnique();
            if (!isSqlite)
            {
                uniqueIndex.AreNullsDistinct(false);
            }
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.InstallMode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<Release>(e =>
        {
            // 平台在应用上、架构在资产上，故版本唯一性回到 (AppId, Version)
            e.HasIndex(x => new { x.AppId, x.Version }).IsUnique();
            // check 热路径（按应用+状态聚合最新版本）：等值列后接版本号排序列，
            // 取代原 (AppId, Status)（其为此索引完整前缀），反向扫描命中 ORDER BY 版本号 DESC
            e.HasIndex(x => new { x.AppId, x.Status, x.VersionMajor, x.VersionMinor, x.VersionPatch });
            e.Property(x => x.Version).HasMaxLength(100);
            e.Property(x => x.MinSupportedVersion).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<ReleaseChannel>(e =>
        {
            e.HasKey(x => new { x.ReleaseId, x.Channel });
            e.HasIndex(x => new { x.Channel, x.Status });
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Release).WithMany().HasForeignKey(x => x.ReleaseId);
            // 关联表无 TenantId：经 Release 归属过滤，与两端实体的过滤器保持一致（消除 EF 对必需端过滤的告警）
            e.HasQueryFilter(x => x.Release.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<ReleaseAsset>(e =>
        {
            // 幂等去重唯一索引：pg NULLS NOT DISTINCT；SQLite 应用层补偿（docs/11 §3.3）
            var uniqueIndex = e.HasIndex(x => new { x.ReleaseId, x.Kind, x.FromVersion, x.PackageType, x.Arch })
                .IsUnique();
            if (!isSqlite)
            {
                uniqueIndex.AreNullsDistinct(false);
            }
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Arch).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PackageType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FromVersion).HasMaxLength(100);
            e.Property(x => x.Url).HasMaxLength(2000);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.Signature).HasMaxLength(4000);
            e.Property(x => x.KeyId).HasMaxLength(100);
            e.Property(x => x.SilentArgs).HasMaxLength(1000);
            e.Property(x => x.InstallArgs).HasMaxLength(1000);
            e.HasOne(x => x.Release).WithMany().HasForeignKey(x => x.ReleaseId);
        });

        modelBuilder.Entity<ReleaseNote>(e =>
        {
            e.HasIndex(x => new { x.ReleaseId, x.Language }).IsUnique();
            e.Property(x => x.Language).HasMaxLength(20);
            if (!isSqlite)
            {
                // pg：原生 text[]；SQLite：List<string> 映射为 TEXT（EF primitive collection，JSON 序列化），无需标注
                e.Property(x => x.New).HasColumnType("text[]");
                e.Property(x => x.Improvements).HasColumnType("text[]");
                e.Property(x => x.Fixes).HasColumnType("text[]");
                e.Property(x => x.Notes).HasColumnType("text[]");
            }
            e.HasOne(x => x.Release).WithMany().HasForeignKey(x => x.ReleaseId);
        });

        modelBuilder.Entity<PackageSource>(e =>
        {
            e.Property(x => x.Provider).HasMaxLength(50);
            e.Property(x => x.Region).HasMaxLength(100);
            e.Property(x => x.Bucket).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<UpdateCheckLog>(e =>
        {
            // (AppId, CheckedAt) 服务"AppId 等值 + 时间范围"的统计聚合；
            // 补时间前导索引服务按保留期的清理（WHERE CheckedAt < cutoff，无 AppId 谓词）
            e.HasIndex(x => new { x.AppId, x.CheckedAt });
            e.HasIndex(x => new { x.CheckedAt, x.AppId });
            e.HasIndex(x => x.InstallationId);
            e.Property(x => x.InstallationId).HasMaxLength(64);
            e.Property(x => x.ProjectKey).HasMaxLength(100);
            e.Property(x => x.AppKey).HasMaxLength(200);
            e.Property(x => x.Platform).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Arch).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CurrentVersion).HasMaxLength(100);
            e.Property(x => x.LatestVersion).HasMaxLength(100);
            e.Property(x => x.ClientIp).HasMaxLength(45);

            // 系统设置：Key 即主键（全局唯一，行数个位数，无租户维度）
            modelBuilder.Entity<Setting>(e =>
            {
                e.HasKey(x => x.Key);
                e.Property(x => x.Key).HasMaxLength(100);
                e.Property(x => x.Value).IsRequired();
                e.Property(x => x.UpdatedBy).HasMaxLength(64);
            });
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.Property(x => x.Actor).HasMaxLength(200);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            // pg jsonb（审计详情仅展示、无 jsonb 运算符依赖）；SQLite 退化为 TEXT（§3.3）
            if (!isSqlite)
            {
                e.Property(x => x.Details).HasColumnType("jsonb");
            }
        });

        modelBuilder.Entity<Installation>(e =>
        {
            e.HasIndex(x => new { x.AppId, x.InstallationId }).IsUnique();
            e.HasIndex(x => new { x.AppId, x.LastSeenAt });
            e.Property(x => x.InstallationId).HasMaxLength(64);
            e.Property(x => x.MachineFingerprintHash).HasMaxLength(64);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastVersion).HasMaxLength(100);
            e.HasOne(x => x.App).WithMany().HasForeignKey(x => x.AppId);
        });

        modelBuilder.Entity<DownloadLog>(e =>
        {
            // 与 UpdateCheckLog 同理：(AppId, DownloadedAt) 服务统计聚合，
            // (DownloadedAt, AppId) 服务按保留期的清理（WHERE DownloadedAt < cutoff）
            e.HasIndex(x => new { x.AppId, x.DownloadedAt });
            e.HasIndex(x => new { x.DownloadedAt, x.AppId });
            e.HasIndex(x => x.InstallationId);
            e.Property(x => x.InstallationId).HasMaxLength(64);
            e.Property(x => x.FromVersion).HasMaxLength(100);
            e.Property(x => x.ToVersion).HasMaxLength(100);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
        });

        modelBuilder.Entity<InstallationReport>(e =>
        {
            // 幂等去重（03 §4）：直传与补报可能重复。pg NULLS NOT DISTINCT；
            // SQLite 唯一索引恒 NULLS DISTINCT，应用层先查补偿保证重复上报仍返回 Duplicate=true（docs/11 §3.3）
            var uniqueIndex = e.HasIndex(x => new { x.AppId, x.InstallationId, x.ToVersion, x.Stage, x.ErrorCode })
                .IsUnique();
            if (!isSqlite)
            {
                uniqueIndex.AreNullsDistinct(false);
            }
            e.Property(x => x.InstallationId).HasMaxLength(64);
            e.Property(x => x.FromVersion).HasMaxLength(100);
            e.Property(x => x.ToVersion).HasMaxLength(100);
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.InstallMode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
        });

        // 多租户 Global Query Filter：TenantContext 未设置（null）时匹配空集
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AtomUpdateDbContext)
                    .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);
                method.Invoke(this, [modelBuilder]);
            }

            // IdGen 主键：所有 long 类型 Id 使用客户端生成（None 关闭 Npgsql IDENTITY 约定；SQLite 无 IDENTITY 概念，跳过）
            if (entityType.FindProperty("Id") is { ClrType: var clrType } idProperty && clrType == typeof(long))
            {
                idProperty.SetValueGeneratorFactory((_, _) => new IdGenValueGenerator(_idGenerator));
                if (!isSqlite)
                {
                    idProperty.SetValueGenerationStrategy(NpgsqlValueGenerationStrategy.None);
                }
            }
        }

        // SQLite：全模型 DateTimeOffset → long(UtcTicks)（INTEGER 列，见 SqliteDateTimeOffsetUtcTicksConverter）。
        // SQLite provider 默认 TEXT 映射且无法翻译 DateTimeOffset 的 SQL 比较/排序（清理与统计查询会抛翻译异常），
        // 整数比较即时间先后，写入前统一归一 UTC（docs/11 §3.3）；可空属性由 EF 在转换器外直通 null。
        if (isSqlite)
        {
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                         .SelectMany(entityType => entityType.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(SqliteDateTimeOffsetUtcTicksConverter.Instance);
            }
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantScoped =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
}
