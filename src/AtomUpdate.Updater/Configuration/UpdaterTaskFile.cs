namespace AtomUpdate.Updater.Configuration;

/// <summary>任务文件（04 §5.1 临时配置 + 运行期字段）：SDK 经 --config 传入；自升级交接时序列化已解析结果复用同一格式。
/// 顶层为运行字段，节对象与 updater.default.json 同构（五层合并后反序列化到本模型）。</summary>
public sealed class UpdaterTaskFile
{
    // ---- 运行字段（SDK ↔ 更新器协议 §5.1）----
    public string? PackagePath { get; set; }

    public string? TargetPath { get; set; }

    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    /// <summary>zip/exe/msi/dmg/pkg/appimage/deb/rpm。</summary>
    public string? PackageType { get; set; }

    /// <summary>manual/onRestart/immediate（小写，仅记录与回传）。</summary>
    public string? InstallMode { get; set; }

    /// <summary>full/delta；缺省按包内容自动识别（含 delete.json+manifest.json 即 delta）。</summary>
    public string? Kind { get; set; }

    public string? Sha256 { get; set; }

    public string? Signature { get; set; }

    public string? KeyId { get; set; }

    public string? RestartPath { get; set; }

    public string? RestartArgs { get; set; }

    public string? ServerUrl { get; set; }

    public string? InstallationId { get; set; }

    public string? MachineFingerprint { get; set; }

    /// <summary>应用标识（状态目录隔离 / 互斥命名 / 回传内容）。</summary>
    public string? AppId { get; set; }

    public string? ProjectKey { get; set; }

    /// <summary>windows/macos/linux（签名内容组成，04 §5.1/05 §8）。</summary>
    public string? Platform { get; set; }

    /// <summary>x64/arm64/x86（签名内容组成）。</summary>
    public string? Arch { get; set; }

    /// <summary>exe/msi 静默参数（包资产元数据）。</summary>
    public string? SilentArgs { get; set; }

    /// <summary>exe/msi 附加安装参数（包资产元数据）。</summary>
    public string? InstallArgs { get; set; }

    /// <summary>等待主程序退出秒数（null = 用 restart.waitForExitMs）。</summary>
    public int? WaitForMainProcessExitSeconds { get; set; }

    // ---- 配置节（与 updater.default.json 同构；自升级交接文件会带全量已解析值）----
    public UpdaterSection Updater { get; set; } = new();

    public VerificationSection Verification { get; set; } = new();

    public SelfUpdateSection SelfUpdate { get; set; } = new();

    public ElevationSection Elevation { get; set; } = new();

    public InstallSection Install { get; set; } = new();

    public RollbackSection Rollback { get; set; } = new();

    public RestartSection Restart { get; set; } = new();

    public ReportSection Report { get; set; } = new();

    public NetworkSection Network { get; set; } = new();
}

/// <summary>命令行列选项（04 §4.3）。</summary>
public sealed class CliOptions
{
    public string? Config { get; set; }
    public string? Package { get; set; }
    public string? Target { get; set; }
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }
    public string? Restart { get; set; }
    public string? RestartArgs { get; set; }
    public string? Sha256 { get; set; }
    public string? Signature { get; set; }
    public string? KeyId { get; set; }
    public string? PackageType { get; set; }
    public string? InstallMode { get; set; }
    public string? InstallArgs { get; set; }
    public string? ReplaceOld { get; set; }
    public int? ReplacePid { get; set; }
    public string? ServerUrl { get; set; }
    public string? InstallationId { get; set; }
    public string? MachineFingerprint { get; set; }
    public string? AppId { get; set; }
    public string? ProjectKey { get; set; }
    public string? LogLevel { get; set; }
    public string? LogPath { get; set; }
    public bool DryRun { get; set; }
    public bool NoRestart { get; set; }
    public bool NoRollback { get; set; }
    public bool Verbose { get; set; }
    public bool Help { get; set; }

    /// <summary>已由提权方式重新启动的标记（防重复弹 UAC；程序会以实际 IsElevated 二次确认）。</summary>
    public bool Elevated { get; set; }
}

public static class PackageTypes
{
    public const string Zip = "zip";
    public const string Exe = "exe";
    public const string Msi = "msi";
    public const string Dmg = "dmg";
    public const string Pkg = "pkg";
    public const string AppImage = "appimage";
    public const string Deb = "deb";
    public const string Rpm = "rpm";

    public static readonly string[] All = [Zip, Exe, Msi, Dmg, Pkg, AppImage, Deb, Rpm];

    public static bool IsValid(string value) => All.Contains(value, StringComparer.OrdinalIgnoreCase);
}

public static class InstallModes
{
    public const string Manual = "manual";
    public const string OnRestart = "onrestart";
    public const string Immediate = "immediate";

    public static readonly string[] All = [Manual, OnRestart, Immediate];

    public static bool IsValid(string value) => All.Contains(value, StringComparer.OrdinalIgnoreCase);
}

public static class ReplaceStrategies
{
    public const string Merge = "merge";
    public const string Replace = "replace";
}

public static class HookFailPolicies
{
    public const string Abort = "abort";
    public const string Continue = "continue";
}

public static class ElevationModes
{
    public const string OnDemand = "ondemand";
    public const string Always = "always";
    public const string Never = "never";

    public static readonly string[] All = [OnDemand, Always, Never];

    public static bool IsValid(string value) => All.Contains(value, StringComparer.OrdinalIgnoreCase);
}

/// <summary>钩子阶段（04 §4.8；下载阶段钩子由 SDK 侧承担，更新器负责安装链路 6 阶段）。</summary>
public static class HookStages
{
    public const string PreInstall = "preInstall";
    public const string PostInstall = "postInstall";
    public const string PreReplace = "preReplace";
    public const string PostReplace = "postReplace";
    public const string PreRestart = "preRestart";
    public const string PostRestart = "postRestart";
}
