using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AtomUpdate.Api.Data;

/// <summary>仅供 dotnet-ef 设计时使用（迁移生成）；运行时由 DI 构造。
/// 双 provider（docs/11 §3.2）：Npgsql 集与 SQLite 集按上下文类型隔离发现，故迁移命令须显式选择上下文——
/// Npgsql 集：dotnet ef migrations add X --context AtomUpdateDbContext
/// SQLite：dotnet ef migrations add X --context SqliteAtomUpdateDbContext --output-dir Data/Migrations/Sqlite
///         --namespace AtomUpdate.Api.Data.SqliteMigrations
/// 追加的 `-- --provider npgsql|sqlite` 仅作兼容标注；实际 provider 由 --context 类型决定，错配时启动即失败。</summary>
public sealed class AtomUpdateDbContextFactory :
    IDesignTimeDbContextFactory<AtomUpdateDbContext>,
    IDesignTimeDbContextFactory<SqliteAtomUpdateDbContext>
{
    private const string NpgsqlDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=atomupdate;Username=postgres;Password=123456789";

    // 设计时占位库（migrations add 不开连接；database update 前请自行替换为真实连接串）
    private const string SqliteDesignTimeConnectionString = "Data Source=atomupdate-design-time.db";

    AtomUpdateDbContext IDesignTimeDbContextFactory<AtomUpdateDbContext>.CreateDbContext(string[] args)
    {
        RequireProvider(args, DatabaseProviderKind.Npgsql);
        return new AtomUpdateDbContext(
            new DbContextOptionsBuilder<AtomUpdateDbContext>().UseNpgsql(NpgsqlDesignTimeConnectionString).Options,
            new TenantContext(), new IdGenerator(0));
    }

    SqliteAtomUpdateDbContext IDesignTimeDbContextFactory<SqliteAtomUpdateDbContext>.CreateDbContext(string[] args)
    {
        RequireProvider(args, DatabaseProviderKind.Sqlite);
        return new SqliteAtomUpdateDbContext(
            new DbContextOptionsBuilder<SqliteAtomUpdateDbContext>().UseSqlite(SqliteDesignTimeConnectionString).Options,
            new TenantContext(), new IdGenerator(0));
    }

    /// <summary>--provider 与 --context 上下文类型错配时拒绝生成，防止迁移写错集合。</summary>
    private static void RequireProvider(string[] args, DatabaseProviderKind contextProvider)
    {
        var declared = ParseProvider(args);
        if (declared is not null && declared != contextProvider)
        {
            throw new InvalidOperationException(
                $"--provider {declared} 与所选上下文不匹配：Npgsql 集用 --context AtomUpdateDbContext，" +
                "SQLite 集用 --context SqliteAtomUpdateDbContext（docs/11 §3.2）");
        }
    }

    private static DatabaseProviderKind? ParseProvider(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var value = args[i].StartsWith("--provider=", StringComparison.Ordinal)
                ? args[i]["--provider=".Length..]
                : args[i] == "--provider" && i + 1 < args.Length ? args[i + 1] : null;
            if (value is null)
            {
                continue;
            }

            return value.ToLowerInvariant() switch
            {
                "npgsql" => DatabaseProviderKind.Npgsql,
                "sqlite" => DatabaseProviderKind.Sqlite,
                var other => throw new InvalidOperationException($"不支持的 --provider 值：{other}（可选 npgsql | sqlite）")
            };
        }

        return null;
    }
}
