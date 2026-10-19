using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;

namespace AtomUpdate.Abstractions.Models;

/// <summary>检查上下文：单次 /api/check 请求的全部输入。</summary>
public sealed class CheckContext
{
    public required UpdateTrigger Trigger { get; init; }

    public required string ProjectKey { get; init; }

    public required string AppId { get; init; }

    public required string CurrentVersion { get; init; }

    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;

    public UpdatePlatform Platform { get; init; } = UpdatePlatform.Windows;

    public UpdateArch Arch { get; init; } = UpdateArch.X64;

    public required string InstallationId { get; init; }

    /// <summary>本地已应用的策略版本（服务端据此判断策略是否变更）。</summary>
    public int PolicyVersion { get; init; }

    /// <summary>上次响应 ETag（命中则服务端 304）。</summary>
    public string? ETag { get; init; }
}

/// <summary>策略缓存模型（04 §3.8 CachedPolicy）。</summary>
public sealed class CachedPolicy
{
    public UpdatePolicy? Policy { get; init; }

    public int PolicyVersion { get; init; }

    public string? ETag { get; init; }

    public string? LatestVersion { get; init; }

    public DateTimeOffset? LastCheckedAt { get; init; }

    /// <summary>上次 200 响应的完整检查负载（304 时重建响应用）。</summary>
    public CheckPayload? LastPayload { get; init; }
}

/// <summary>安装上下文：门面向 IUpdateInstaller 提交的安装请求。</summary>
public sealed class InstallContext
{
    /// <summary>校验通过的包文件路径。</summary>
    public required string PackagePath { get; init; }

    public required UpdateInfo Update { get; init; }

    public InstallMode Mode { get; init; }

    /// <summary>主程序可执行文件路径（更新器安装完成后重启）。</summary>
    public required string RestartPath { get; init; }

    public string RestartArgs { get; init; } = "--skip-update-check";
}

/// <summary>通知上下文。</summary>
public sealed class NotificationContext
{
    public required NotificationType Type { get; init; }

    public string? Version { get; init; }

    /// <summary>标题模板占位替换后的标题。</summary>
    public string? Title { get; init; }

    public string? Body { get; init; }

    public ReleaseNotes? ReleaseNotes { get; init; }

    /// <summary>强制更新标记（强制通知不可关闭）。</summary>
    public bool ForceUpdate { get; init; }
}
