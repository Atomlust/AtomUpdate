using AtomUpdate.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtomUpdate.Tests.Api;

[CollectionDefinition("api-setup", DisableParallelization = true)]
public sealed class SetupApiCollection : ICollectionFixture<SetupApiFixture>;

/// <summary>零配置部署 fixture：SQLite 临时库 + 不预配 Auth:Bootstrap——模拟"首次启动走向导"形态，
/// 专测设置向导端点（docs/09）。与主 fixture 的 pg 库隔离；进程环境变量按顺序独占（-m:1）。</summary>
public sealed class SetupApiFixture : ApiHostFixtureBase, IAsyncLifetime
{
    private string? _dbPath;

    async Task IAsyncLifetime.InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"atomupdate-setup-tests-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("Database__Provider", "sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("Auth__Jwt__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Auth__Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Auth__Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Cache__ReleaseTtlSeconds", "1");
        Environment.SetEnvironmentVariable("Cache__ReadTtlSeconds", "1");
        // 模拟零配置部署：显式覆盖为空串（优先级高于 Web 宿主 appsettings.Development.json 预置的
        // Auth:Bootstrap，也压掉同进程其他 fixture 残留的环境变量）——空值即向导路径
        Environment.SetEnvironmentVariable("Auth__Bootstrap__Email", "");
        Environment.SetEnvironmentVariable("Auth__Bootstrap__Password", "");
        Environment.SetEnvironmentVariable("Auth__Bootstrap__UserName", "");
        Environment.SetEnvironmentVariable("Auth__Bootstrap__TenantSlug", "");

        CreateClientNow();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SqliteAtomUpdateDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        Environment.SetEnvironmentVariable("Database__Provider", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", null);
        SqliteConnection.ClearAllPools();
        if (_dbPath is not null)
        {
            try { File.Delete(_dbPath); } catch (IOException) { /* 独占句柄未释放时留给系统临时目录 */ }
        }
    }
}
