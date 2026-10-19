using AtomUpdate.Core.Constants;

namespace AtomUpdate.Tests.Core;

public class ConstantsTests
{
    [Fact]
    public void Exit_codes_match_contract()
    {
        Assert.Equal(0x0000, ExitCodes.Success);
        Assert.Equal(0x1001, ExitCodes.MutexAcquireFailed);
        Assert.Equal(0x1002, ExitCodes.ArgumentError);
        Assert.Equal(0x1003, ExitCodes.ElevationRejected);
        Assert.Equal(0x1004, ExitCodes.VerificationFailed);
        Assert.Equal(0x1005, ExitCodes.BackupFailed);
        Assert.Equal(0x1006, ExitCodes.ReplaceFailed);
        Assert.Equal(0x1007, ExitCodes.InstallerFailed);
        Assert.Equal(0x1008, ExitCodes.HookFailed);
        Assert.Equal(0x1009, ExitCodes.RollbackFailed);
        Assert.Equal(0x100A, ExitCodes.RestartFailed);
        Assert.Equal(0x100B, ExitCodes.Unknown);
    }

    [Fact]
    public void Api_routes_match_contract()
    {
        Assert.Equal("/api/check", ApiRoutes.Check);
        Assert.Equal("/api/report/install", ApiRoutes.ReportInstall);
        Assert.Equal("/api/stats/install", ApiRoutes.StatsInstall);
        Assert.Equal("/api/stats/check", ApiRoutes.StatsCheck);
        Assert.Equal("/api/stats/download", ApiRoutes.StatsDownload);
        Assert.Equal("/api/stats/batch", ApiRoutes.StatsBatch);
        Assert.Equal("/health", ApiRoutes.Health);
    }

    [Fact]
    public void Header_names_match_contract()
    {
        Assert.Equal("User-Agent", HeaderNames.UserAgent);
        Assert.Equal("If-None-Match", HeaderNames.IfNoneMatch);
        Assert.Equal("ETag", HeaderNames.ETag);
    }

    [Fact]
    public void Server_error_codes_match_contract()
    {
        Assert.Equal("INVALID_REQUEST", ErrorCodes.InvalidRequest);
        Assert.Equal("INVALID_VERSION", ErrorCodes.InvalidVersion);
        Assert.Equal("APP_NOT_FOUND", ErrorCodes.AppNotFound);
        Assert.Equal("INVALID_CHANNEL", ErrorCodes.InvalidChannel);
        Assert.Equal("UNAUTHORIZED", ErrorCodes.Unauthorized);
    }

    [Fact]
    public void Sdk_error_codes_match_contract()
    {
        Assert.Equal("CHECK_FAILED", ErrorCodes.CheckFailed);
        Assert.Equal("NETWORK_ERROR", ErrorCodes.NetworkError);
        Assert.Equal("SERVER_ERROR", ErrorCodes.ServerError);
        Assert.Equal("INVALID_RESPONSE", ErrorCodes.InvalidResponse);
        Assert.Equal("DOWNLOAD_FAILED", ErrorCodes.DownloadFailed);
        Assert.Equal("VERIFY_FAILED", ErrorCodes.VerifyFailed);
        Assert.Equal("INSTALL_FAILED", ErrorCodes.InstallFailed);
        Assert.Equal("ELEVATION_REJECTED", ErrorCodes.ElevationRejected);
        Assert.Equal("UPDATER_NOT_FOUND", ErrorCodes.UpdaterNotFound);
        Assert.Equal("STATE_INVALID", ErrorCodes.StateInvalid);
        Assert.Equal("UNKNOWN", ErrorCodes.Unknown);
    }
}
