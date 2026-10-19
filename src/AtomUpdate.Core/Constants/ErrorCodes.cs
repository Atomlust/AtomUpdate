namespace AtomUpdate.Core.Constants;

public static class ErrorCodes
{
    // 服务端错误码
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InvalidVersion = "INVALID_VERSION";
    public const string TenantNotFound = "TENANT_NOT_FOUND";
    public const string ProjectNotFound = "PROJECT_NOT_FOUND";
    public const string AppNotFound = "APP_NOT_FOUND";
    public const string InvalidChannel = "INVALID_CHANNEL";
    public const string PlatformMismatch = "PLATFORM_MISMATCH";
    public const string ReleaseNotFound = "RELEASE_NOT_FOUND";
    public const string ReleaseExists = "RELEASE_EXISTS";
    public const string ReleaseWithdrawn = "RELEASE_WITHDRAWN";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string UserExists = "USER_EXISTS";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string Conflict = "CONFLICT";

    // SDK 侧错误码
    public const string CheckFailed = "CHECK_FAILED";
    public const string NetworkError = "NETWORK_ERROR";
    public const string ServerError = "SERVER_ERROR";
    public const string InvalidResponse = "INVALID_RESPONSE";
    public const string DownloadFailed = "DOWNLOAD_FAILED";
    public const string VerifyFailed = "VERIFY_FAILED";
    public const string InstallFailed = "INSTALL_FAILED";
    public const string ElevationRejected = "ELEVATION_REJECTED";
    public const string UpdaterNotFound = "UPDATER_NOT_FOUND";
    public const string StateInvalid = "STATE_INVALID";
    public const string Unknown = "UNKNOWN";
}
