using AtomUpdate.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtomUpdate.Tests.Api;

/// <summary>SQLite 冒烟测试集合：与 "api"（PostgreSQL）共进程但禁并行，避免注册期环境变量互踩。</summary>
[CollectionDefinition("api-sqlite", DisableParallelization = true)]
public sealed class SqliteApiCollection : ICollectionFixture<SqliteApiFixture>;

/// <summary>全栈集成测试宿主（SQLite，docs/11 §5）：临时文件库 + WebApplicationFactory&lt;Program&gt;，
/// 验证启动迁移、check/report/manage 核心路径与保留清理在 SQLite 下的行为。</summary>
public sealed class SqliteApiFixture : ApiHostFixtureBase, IAsyncLifetime
{
    private string? _dbPath;

    async Task IAsyncLifetime.InitializeAsync()
    {
        // 与 ApiFixture 同机制：注册期配置读取走进程环境变量；Database__Provider 显式固定（同进程双宿主防互踩）
        _dbPath = Path.Combine(Path.GetTempPath(), $"atomupdate-sqlite-tests-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("Database__Provider", "sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("Auth__Jwt__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Auth__Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Auth__Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Auth__Bootstrap__Email", BootstrapEmail);
        Environment.SetEnvironmentVariable("Auth__Bootstrap__Password", BootstrapPassword);
        Environment.SetEnvironmentVariable("Cache__ReleaseTtlSeconds", "1");
        Environment.SetEnvironmentVariable("Cache__ReadTtlSeconds", "1");

        // 立即物化宿主（环境变量在此固定）；Development 环境自动迁移可能不执行，显式建库——
        // SQLite 迁移集按上下文类型隔离发现（docs/11 §3.2），须用派生上下文
        CreateClientNow();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SqliteAtomUpdateDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        // 归还进程默认 provider；连接池关闭后再清理临时库文件
        Environment.SetEnvironmentVariable("Database__Provider", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", null);
        SqliteConnection.ClearAllPools();
        if (_dbPath is not null)
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // 文件被占用时放弃删除（临时目录，系统回收）
            }
        }
    }
}
