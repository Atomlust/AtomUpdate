namespace AtomUpdate.Core.Constants;

public static class ExitCodes
{
    public const int Success = 0x0000;
    public const int MutexAcquireFailed = 0x1001;
    public const int ArgumentError = 0x1002;
    public const int ElevationRejected = 0x1003;
    public const int VerificationFailed = 0x1004;
    public const int BackupFailed = 0x1005;
    public const int ReplaceFailed = 0x1006;
    public const int InstallerFailed = 0x1007;
    public const int HookFailed = 0x1008;
    public const int RollbackFailed = 0x1009;
    public const int RestartFailed = 0x100A;
    public const int Unknown = 0x100B;
}
