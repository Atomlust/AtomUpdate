using System.Data;
using System.Net;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Api.Data;
using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Api.Endpoints.Reports;
using AtomUpdate.Api.Telemetry;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtomUpdate.Tests.Api;

/// <summary>SQLite 冒烟测试（docs/11 §5）：启动迁移、check 热路径、manage CRUD、report 幂等、保留清理、
/// WAL PRAGMA 与 NULLS DISTINCT 补偿——完整管线复用与 PostgreSQL 套件相同的宿主与端点。</summary>
[Collection("api-sqlite")]
public class SqliteSmokeTests(SqliteApiFixture fixture)
{
    [Fact]
    public async Task Database_IsSqlite_Migrated_And_WalEnabled()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtomUpdateDbContext>();

        Assert.True(await db.Database.CanConnectAsync());
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);

        // 启动迁移成功（独立迁移集：派生上下文发现）
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, migration => migration.EndsWith("InitialCreate", StringComparison.Ordinal));

        // 连接打开 PRAGMA 生效：WAL + foreign_keys（ExecuteScalar 返回 SQLite 整数，统一按 long 比较）
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        Assert.Equal("wal", QueryPragma(connection, "PRAGMA journal_mode"));
        Assert.Equal(1L, QueryPragma(connection, "PRAGMA foreign_keys"));
    }

    [Fact]
    public async Task Check_FullPath_ServesUpdate_And_WritesCheckLog()
    {
        // manage CRUD（租户/项目/应用/策略 + 发布）走 SQLite 落库
        var scenario = await fixture.CreateScenarioAsync();
        await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario,
            ApiHostFixtureBase.WindowsRelease("2.0.0",
                ApiHostFixtureBase.FullAsset("http://assets.test/full.zip", ApiHostFixtureBase.Sha('a'))));

        // check 热路径：返回新版本并命中策略
        var result = await fixture.CheckAsync(ApiHostFixtureBase.CheckBody(scenario, "1.0.0"));
        var response = result.Expect(HttpStatusCode.OK).Data;
        Assert.True(response.HasUpdate);
        Assert.Equal("2.0.0", response.LatestVersion);

        // 后台批量落库（CheckLogWriterService）在 SQLite 下写入成功
        await fixture.WaitForDbAsync(scenario.TenantId, db => db.UpdateCheckLogs
            .AnyAsync(log => log.AppId == scenario.AppId && log.HasUpdate));
    }

    [Fact]
    public async Task ManageCrud_Notes_Assets_And_Policy_RoundTrip()
    {
        var scenario = await fixture.CreateScenarioAsync(allowForceUpdate: true);
        var release = await fixture.CreateAndPublishAsync(scenario.AdminToken, scenario, new ReleaseCreateRequest
        {
            Version = "2.1.0",
            Assets =
            [
                ApiHostFixtureBase.FullAsset("http://assets.test/full.zip", ApiHostFixtureBase.Sha('b')),
                ApiHostFixtureBase.DeltaAsset("2.0.0", "http://assets.test/delta.zip", ApiHostFixtureBase.Sha('c'))
            ],
            // List<string> 列在 SQLite 下为 TEXT（JSON 序列化），读回须还原
            Notes = new ReleaseNoteInput
            {
                Language = "zh",
                New = ["新功能一", "新功能二"],
                Fixes = ["修复崩溃"]
            }
        });

        var notes = await fixture.QueryAsync(scenario.TenantId, db => db.ReleaseNotes
            .AsNoTracking().SingleAsync(note => note.ReleaseId == release.Id));
        Assert.Equal(["新功能一", "新功能二"], notes.New);
        Assert.Equal(["修复崩溃"], notes.Fixes);

        // 资产读回：Delta.FromVersion 与 Full.FromVersion（NULL）语义
        var assets = await fixture.QueryAsync(scenario.TenantId, db => db.ReleaseAssets
            .AsNoTracking().Where(asset => asset.ReleaseId == release.Id).OrderBy(asset => asset.Kind).ToListAsync());
        Assert.Equal(2, assets.Count);
        Assert.Null(assets.Single(asset => asset.Kind == AssetKind.Full).FromVersion);
        Assert.Equal("2.0.0", assets.Single(asset => asset.Kind == AssetKind.Delta).FromVersion);

        // 策略 PATCH（UpdatePolicy 读改写路径）
        var policy = await fixture.UpdatePolicyAsync(scenario.AdminToken, scenario.AppId, new PolicyUpdateRequest
        {
            AllowForceUpdate = true,
            AllowDowngrade = true
        });
        Assert.True(policy.AllowDowngrade);
    }

    [Fact]
    public async Task ReportInstall_IsIdempotent_OnSqlite()
    {
        // SQLite 唯一索引恒 NULLS DISTINCT（ErrorCode NULL），重复上报须仍返回 Duplicate=true（docs/11 §3.3 应用层补偿）
        var scenario = await fixture.CreateScenarioAsync();
        var installationId = Guid.NewGuid().ToString("D");
        var body = new InstallReport
        {
            ProjectKey = scenario.ProjectKey,
            AppId = scenario.AppBusinessId,
            InstallationId = installationId,
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            Stage = UpdateStage.Completed,
            InstallMode = InstallMode.Immediate,
            Elevated = true
        };

        var first = (await fixture.SendJsonAsync<ReportInstallResponse>(HttpMethod.Post, ApiRoutes.ReportInstall, body))
            .Expect(HttpStatusCode.OK).Data;
        var second = (await fixture.SendJsonAsync<ReportInstallResponse>(HttpMethod.Post, ApiRoutes.ReportInstall, body))
            .Expect(HttpStatusCode.OK).Data;

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(1, await fixture.QueryAsync(scenario.TenantId, db => db.InstallationReports
            .CountAsync(report => report.InstallationId == installationId)));
    }

    [Fact]
    public async Task RetentionCleanup_RemovesExpiredLogs()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var tenantId = scenario.TenantId;
        var expiredAt = DateTimeOffset.UtcNow.AddDays(-8);
        var recentAt = DateTimeOffset.UtcNow;
        var expiredInstallation = Guid.NewGuid().ToString("D");
        var recentInstallation = Guid.NewGuid().ToString("D");

        await fixture.QueryAsync(tenantId, async db =>
        {
            db.UpdateCheckLogs.AddRange(
                new UpdateCheckLog
                {
                    TenantId = tenantId, AppId = scenario.AppId, InstallationId = expiredInstallation,
                    ProjectKey = scenario.ProjectKey, AppKey = scenario.AppBusinessId,
                    Platform = UpdatePlatform.Windows, Arch = UpdateArch.X64,
                    CurrentVersion = "1.0.0", CheckedAt = expiredAt
                },
                new UpdateCheckLog
                {
                    TenantId = tenantId, AppId = scenario.AppId, InstallationId = recentInstallation,
                    ProjectKey = scenario.ProjectKey, AppKey = scenario.AppBusinessId,
                    Platform = UpdatePlatform.Windows, Arch = UpdateArch.X64,
                    CurrentVersion = "1.0.0", CheckedAt = recentAt
                });
            await db.SaveChangesAsync();
            return true;
        });

        // SQLite 分支：先查主键后按主键删除（无 DELETE ... LIMIT 翻译）
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LogRetentionCleanupService>()
            .CleanupAsync(CancellationToken.None);

        Assert.Equal(0, await fixture.QueryAsync(tenantId, db => db.UpdateCheckLogs
            .CountAsync(log => log.InstallationId == expiredInstallation)));
        Assert.Equal(1, await fixture.QueryAsync(tenantId, db => db.UpdateCheckLogs
            .CountAsync(log => log.InstallationId == recentInstallation)));
    }

    private static object QueryPragma(SqliteConnection connection, string pragma)
    {
        var opened = connection.State == ConnectionState.Open;
        if (!opened)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            return command.ExecuteScalar()!;
        }
        finally
        {
            if (!opened)
            {
                connection.Close();
            }
        }
    }
}
