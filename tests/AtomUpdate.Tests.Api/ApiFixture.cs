using System.Net;
using AtomUpdate.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AtomUpdate.Tests.Api;

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;

/// <summary>全栈集成测试宿主（PostgreSQL）：Testcontainers PostgreSQL 17 + WebApplicationFactory&lt;Program&gt;
/// （真实管线：鉴权/租户中间件/端点/后台日志写入）。</summary>
public sealed class ApiFixture : ApiHostFixtureBase, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _container.StartAsync();

        // Program.cs 的注册期配置读取（AddDbContext / AddAtomUpdateAuth）发生在 WebApplicationFactory 的
        // ConfigureAppConfiguration 生效之前，必须走默认配置链（进程环境变量）才能被读取到；
        // Database__Provider 亦显式固定：与 SqliteApiFixture 共进程，防止跨宿主配置互踩
        Environment.SetEnvironmentVariable("Database__Provider", "npgsql");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("Auth__Jwt__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Auth__Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Auth__Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Auth__Bootstrap__Email", BootstrapEmail);
        Environment.SetEnvironmentVariable("Auth__Bootstrap__Password", BootstrapPassword);
        Environment.SetEnvironmentVariable("Cache__ReleaseTtlSeconds", "1");
        Environment.SetEnvironmentVariable("Cache__ReadTtlSeconds", "1");

        // 立即物化宿主（环境变量在此固定）；入口点中断机制下不保证执行 Program.cs 的 Development 自动迁移，显式建库
        CreateClientNow();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}
