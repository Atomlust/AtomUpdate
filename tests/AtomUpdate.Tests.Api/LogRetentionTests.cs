using AtomUpdate.Api.Data.Entities;
using AtomUpdate.Api.Telemetry;
using AtomUpdate.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtomUpdate.Tests.Api;

/// <summary>运行日志保留清理：超过 Logging:RetentionDays（默认 7 天）的三类日志被删除，未到期记录保留。</summary>
[Collection("api")]
public class LogRetentionTests(ApiFixture fixture)
{
    [Fact]
    public async Task CleanupAsync_RemovesExpired_And_KeepsRecent()
    {
        var scenario = await fixture.CreateScenarioAsync();
        var expiredAt = DateTimeOffset.UtcNow.AddDays(-8);
        var recentAt = DateTimeOffset.UtcNow;
        var expiredInstallation = Guid.NewGuid().ToString("D");
        var recentInstallation = Guid.NewGuid().ToString("D");
        var tenantId = scenario.TenantId;

        await fixture.QueryAsync(tenantId, async db =>
        {
            db.UpdateCheckLogs.AddRange(
                new UpdateCheckLog
                {
                    TenantId = tenantId,
                    AppId = scenario.AppId,
                    InstallationId = expiredInstallation,
                    ProjectKey = scenario.ProjectKey,
                    AppKey = scenario.AppBusinessId,
                    Platform = UpdatePlatform.Windows,
                    Arch = UpdateArch.X64,
                    CurrentVersion = "1.0.0",
                    CheckedAt = expiredAt
                },
                new UpdateCheckLog
                {
                    TenantId = tenantId,
                    AppId = scenario.AppId,
                    InstallationId = recentInstallation,
                    ProjectKey = scenario.ProjectKey,
                    AppKey = scenario.AppBusinessId,
                    Platform = UpdatePlatform.Windows,
                    Arch = UpdateArch.X64,
                    CurrentVersion = "1.0.0",
                    CheckedAt = recentAt
                });
            db.DownloadLogs.AddRange(
                new DownloadLog
                {
                    TenantId = tenantId,
                    AppId = scenario.AppId,
                    InstallationId = expiredInstallation,
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    DownloadedAt = expiredAt
                },
                new DownloadLog
                {
                    TenantId = tenantId,
                    AppId = scenario.AppId,
                    InstallationId = recentInstallation,
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    DownloadedAt = recentAt
                });
            db.InstallationReports.AddRange(
                new InstallationReport
                {
                    TenantId = tenantId,
                    AppId = scenario.AppId,
                    InstallationId = expiredInstallation,
                    ToVersion = "2.0.0",
                    ReportedAt = expiredAt
                },
                new InstallationReport
                {
                    TenantId = tenantId,
                    AppId = scenario.AppId,
                    InstallationId = recentInstallation,
                    ToVersion = "2.0.0",
                    ReportedAt = recentAt
                });
            await db.SaveChangesAsync();
            return true;
        });

        // 直接触发一轮清理（默认保留 7 天）
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LogRetentionCleanupService>()
            .CleanupAsync(CancellationToken.None);

        // 超过保留期的记录全部清除，未到期记录保留
        Assert.Equal(0, await CountChecksAsync(tenantId, expiredInstallation));
        Assert.Equal(0, await fixture.QueryAsync(tenantId, db => db.DownloadLogs
            .CountAsync(log => log.InstallationId == expiredInstallation)));
        Assert.Equal(0, await fixture.QueryAsync(tenantId, db => db.InstallationReports
            .CountAsync(report => report.InstallationId == expiredInstallation)));

        Assert.Equal(1, await CountChecksAsync(tenantId, recentInstallation));
        Assert.Equal(1, await fixture.QueryAsync(tenantId, db => db.DownloadLogs
            .CountAsync(log => log.InstallationId == recentInstallation)));
        Assert.Equal(1, await fixture.QueryAsync(tenantId, db => db.InstallationReports
            .CountAsync(report => report.InstallationId == recentInstallation)));
    }

    private Task<int> CountChecksAsync(long tenantId, string installationId) =>
        fixture.QueryAsync(tenantId, db => db.UpdateCheckLogs
            .CountAsync(log => log.InstallationId == installationId));
}
