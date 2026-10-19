using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Data;

/// <summary>数据库 provider（docs/11 §3.1）：部署时经 Database:Provider 配置选择，不做运行时切换。</summary>
public enum DatabaseProviderKind
{
    /// <summary>PostgreSQL（多实例/生产形态，生产部署建议，docs/11 决策矩阵）。</summary>
    Npgsql,

    /// <summary>SQLite（零配置默认，轻量零依赖形态，单实例）。</summary>
    Sqlite
}

/// <summary>provider 解析与 DbContext 装配：连接串分别取 ConnectionStrings:Postgres / ConnectionStrings:Sqlite；
/// sqlite 模式附加连接打开 PRAGMA 拦截器（WAL / busy_timeout / foreign_keys，docs/11 §3.4）。</summary>
public static class DatabaseProviders
{
    private static readonly SqliteConnectionPragmasInterceptor SqlitePragmas = SqliteConnectionPragmasInterceptor.Instance;

    /// <summary>解析 Database:Provider（sqlite 默认 | npgsql），非法值启动即失败。</summary>
    public static DatabaseProviderKind Resolve(IConfiguration configuration)
    {
        var value = configuration.GetValue("Database:Provider", nameof(DatabaseProviderKind.Sqlite));
        return value?.ToLowerInvariant() switch
        {
            "npgsql" => DatabaseProviderKind.Npgsql,
            "sqlite" => DatabaseProviderKind.Sqlite,
            var other => throw new InvalidOperationException($"不支持的 Database:Provider 值：{other}（可选 npgsql | sqlite）")
        };
    }

    /// <summary>按 provider 解析连接串（npgsql 缺失即启动失败；sqlite 走零配置默认，见 ResolveSqliteConnectionString）。</summary>
    public static string ResolveConnectionString(IConfiguration configuration, DatabaseProviderKind provider) => provider switch
    {
        DatabaseProviderKind.Npgsql => configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("缺少配置 ConnectionStrings:Postgres"),
        DatabaseProviderKind.Sqlite => ResolveSqliteConnectionString(configuration),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    /// <summary>SQLite 连接串（docs/11 §3.1 零配置默认）：显式 ConnectionStrings:Sqlite 优先；
    /// 未配置时按环境取默认——容器约定数据挂载点 /data 存在则库落数据卷（JWT 密钥文件随之同目录持久化），
    /// 否则落应用根目录相对路径（本地开发）。appsettings 不再内置 Sqlite 默认值，保证零配置状态真实可达。</summary>
    public static string ResolveSqliteConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("Sqlite");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var source = Directory.Exists("/data") ? "/data/atomupdate.db" : "atomupdate.db";
        return new SqliteConnectionStringBuilder { DataSource = source }.ToString();
    }

    /// <summary>按 provider 装配 DbContextOptions（npgsql / sqlite；迁移集随上下文类型隔离，见 SqliteAtomUpdateDbContext）。</summary>
    public static void Configure(DbContextOptionsBuilder options, DatabaseProviderKind provider, string connectionString)
    {
        switch (provider)
        {
            case DatabaseProviderKind.Npgsql:
                options.UseNpgsql(connectionString);
                break;
            case DatabaseProviderKind.Sqlite:
                options.UseSqlite(connectionString).AddInterceptors(SqlitePragmas);
                break;
        }
    }
}
