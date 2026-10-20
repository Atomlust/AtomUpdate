using AtomUpdate.Api.Auth;
using AtomUpdate.Api.Cache;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Endpoints;
using AtomUpdate.Api.Endpoints.Manage;
using AtomUpdate.Api.Endpoints.Releases;
using AtomUpdate.Api.Endpoints.Reports;
using AtomUpdate.Api.Telemetry;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Serialization;
using AtomUpdate.Validation;
using FluentValidation;
using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

namespace AtomUpdate.Api;

/// <summary>Api 装配（docs/02 §2 决策 4：Api 与 Web 单进程托管可拆）——Web 宿主复用同一套 DI/中间件/端点。</summary>
public static class AtomUpdateApiHosting
{
    public static IServiceCollection AddAtomUpdateApi(this IServiceCollection services, IConfiguration configuration,
        string? contentRootPath = null)
    {
        services.ConfigureHttpJsonOptions(options => AtomUpdateJson.ApplyTo(options.SerializerOptions));
        services.AddValidatorsFromAssemblyContaining<CheckRequestValidator>();
        services.AddOpenApi();

        // 数据层：按 Database:Provider 装配（sqlite 默认 | npgsql，docs/11 §3.1）+ 请求级租户上下文 + IdGen 主键
        // （库仅注册具体 IdGenerator，需桥接接口）；主键为 IdGen 应用侧发号，与 provider 无关
        var provider = DatabaseProviders.Resolve(configuration);
        var connectionString = DatabaseProviders.ResolveConnectionString(configuration, provider);
        services.AddDbContext<AtomUpdateDbContext>(options =>
            DatabaseProviders.Configure(options, provider, connectionString));
        if (provider == DatabaseProviderKind.Sqlite)
        {
            // SQLite 迁移集按上下文类型隔离发现（docs/11 §3.2）：派生上下文专用于迁移
            services.AddDbContext<SqliteAtomUpdateDbContext>(options =>
                DatabaseProviders.Configure(options, provider, connectionString));
        }
        services.AddScoped<TenantContext>();
        // 系统设置：平台级运营参数（DB 有值优先，代码默认兜底），清理服务每轮读取
        services.AddScoped<Settings.SettingsService>();
        // IdGen.DependencyInjection 3.0.7 在本应用解析 IdGenerator 时死锁，改为直接注册
        var generatorId = configuration.GetValue("IdGen:GeneratorId", 0);
        services.AddSingleton(new IdGenerator(generatorId));
        services.AddSingleton<IIdGenerator<long>>(sp => sp.GetRequiredService<IdGenerator>());

        // 鉴权：JWT Bearer（管理后台会话；check/report 为公开端点）。contentRoot 供 JWT 密钥文件相对路径解析
        services.AddAtomUpdateAuth(configuration, contentRootPath);
        services.AddSingleton<AuthTokenService>();

        // 读路径发布缓存（发布/撤回 bump epoch 失效；Redis 实现二阶段接入）
        services.AddSingleton<IReleaseCache, MemoryReleaseCache>();

        // 读路径解析/聚合缓存（项目/应用/策略解析与 stats 概览；写路径变更 InvalidateAll 失效）
        services.AddSingleton<IReadCache, MemoryReadCache>();

        // 检查日志：有界队列 + 后台批量落库（/api/check 不等待写入）
        services.AddSingleton<CheckLogQueue>();
        services.AddHostedService<CheckLogWriterService>();

        // 下载上报：有界队列 + 后台批量落库（InstallationReport 幂等语义走同步插入，不经队列）
        services.AddSingleton<ReportQueue>();
        services.AddHostedService<ReportWriterService>();

        // 运行日志保留清理（参数存 Setting 表，系统设置页可配；每 6 小时分批删除防长事务锁表）
        // 同一实例注册为具体类型，供运维/测试直接触发 CleanupAsync
        services.AddSingleton<LogRetentionCleanupService>();
        services.AddHostedService(sp => sp.GetRequiredService<LogRetentionCleanupService>());

        // 响应压缩：Brotli + Gzip（默认提供器），EnableForHttps=false —— HTTPS 场景交由反代处理（docs/09）
        // MIME 覆盖默认集合之外的 JSON API 与 wasm；text/* 通配交由中间件匹配
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = false;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
            [
                "text/*",
                "application/json",
                "application/wasm"
            ]);
        });

        return services;
    }

    /// <summary>鉴权中间件 + 开发环境迁移 + 平台管理员引导。在 UseAntiforgery 之前调用。</summary>
    public static async Task UseAtomUpdateApiAsync(this WebApplication app)
    {
        // 反代场景还原真实客户端 IP（检测记录/审计）：私有化内网反代地址不固定，信任全部转发网络。
        // ClientIp 仅作日志记录用途，不参与任何安全决策（docs/09 反代部署）。
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        app.UseForwardedHeaders(forwarded);

        // 响应压缩须先于所有写响应的环节（端点/静态资源）注册；HTTPS 由 EnableForHttps=false 跳过
        app.UseResponseCompression();

        // 启动迁移：开发环境总是执行；部署形态默认随 provider——sqlite 零配置形态默认开启（幂等、单写者无并发迁移），
        // npgsql 多实例防并发迁移默认关闭，可 Database:MigrateOnStartup=true 显式开启（docs/09）
        var providerKind = DatabaseProviders.Resolve(app.Configuration);
        var migrateOnStartup = app.Configuration.GetValue("Database:MigrateOnStartup", providerKind == DatabaseProviderKind.Sqlite);
        if (app.Environment.IsDevelopment() || migrateOnStartup)
        {
            using var scope = app.Services.CreateScope();
            // 迁移发现按上下文类型隔离（docs/11 §3.2）：sqlite 须用派生上下文，npgsql 用主上下文
            if (providerKind == DatabaseProviderKind.Sqlite)
            {
                await scope.ServiceProvider.GetRequiredService<SqliteAtomUpdateDbContext>().Database.MigrateAsync();
            }
            else
            {
                await scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>().Database.MigrateAsync();
            }
        }

        // 首个平台管理员引导：用户表为空且 Auth:Bootstrap 已配置时幂等种子
        await AuthBootstrapSeeder.RunAsync(app.Services);

        app.UseAuthentication();
        app.UseMiddleware<TenantContextMiddleware>();
        app.UseAuthorization();
    }

    public static void MapAtomUpdateApi(this WebApplication app)
    {
        app.MapOpenApi();

        app.MapGet(ApiRoutes.Health, () => Results.Ok(new HealthResponse()))
            .Produces<HealthResponse>()
            .WithTags("ops");

        app.MapPost(ApiRoutes.Check, CheckEndpoint.Handle)
            .Produces<CheckResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithTags("check");

        app.MapManageEndpoints();
        app.MapReleaseEndpoints();
        app.MapReportEndpoints();
    }
}

public sealed class HealthResponse
{
    public string Status { get; init; } = "ok";
}
