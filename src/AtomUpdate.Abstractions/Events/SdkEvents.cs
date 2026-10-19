using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Core.Models;

namespace AtomUpdate.Abstractions.Events;

/// <summary>检查完成（成功或失败均触发）。</summary>
public sealed class CheckCompletedEventArgs(CheckResult result) : EventArgs
{
    public CheckResult Result { get; } = result;
}

/// <summary>检查发现有可用更新。</summary>
public sealed class UpdateAvailableEventArgs(UpdateInfo update) : EventArgs
{
    public UpdateInfo Update { get; } = update;
}

/// <summary>开始下载。</summary>
public sealed class DownloadStartedEventArgs(UpdateInfo update) : EventArgs
{
    public UpdateInfo Update { get; } = update;
}

/// <summary>下载进度变化（后台线程触发，UI 层自行调度）。</summary>
public sealed class DownloadProgressChangedEventArgs(DownloadProgress progress) : EventArgs
{
    public DownloadProgress Progress { get; } = progress;
}

/// <summary>下载完成（校验通过）。</summary>
public sealed class DownloadCompletedEventArgs(UpdateInfo update, string packagePath) : EventArgs
{
    public UpdateInfo Update { get; } = update;

    public string PackagePath { get; } = packagePath;
}

/// <summary>下载失败。</summary>
public sealed class DownloadFailedEventArgs(string? errorCode, string? errorMessage, Exception? exception) : EventArgs
{
    public string? ErrorCode { get; } = errorCode;

    public string? ErrorMessage { get; } = errorMessage;

    public Exception? Exception { get; } = exception;
}

/// <summary>包就绪可安装。</summary>
public sealed class UpdateReadyEventArgs(UpdateInfo update, string packagePath) : EventArgs
{
    public UpdateInfo Update { get; } = update;

    public string PackagePath { get; } = packagePath;
}

/// <summary>安装已调度/启动。</summary>
public sealed class InstallStartedEventArgs(UpdateInfo update) : EventArgs
{
    public UpdateInfo Update { get; } = update;
}

/// <summary>安装完成（更新器回执 success）。</summary>
public sealed class InstallCompletedEventArgs(UpdateInfo update) : EventArgs
{
    public UpdateInfo Update { get; } = update;
}

/// <summary>安装失败。</summary>
public sealed class InstallFailedEventArgs(string? errorCode, string? errorMessage, Exception? exception) : EventArgs
{
    public string? ErrorCode { get; } = errorCode;

    public string? ErrorMessage { get; } = errorMessage;

    public Exception? Exception { get; } = exception;
}

/// <summary>策略变更（服务端下发合并后触发）。</summary>
public sealed class PolicyChangedEventArgs(UpdatePolicy? oldPolicy, UpdatePolicy newPolicy) : EventArgs
{
    public UpdatePolicy? OldPolicy { get; } = oldPolicy;

    public UpdatePolicy NewPolicy { get; } = newPolicy;
}

/// <summary>状态机状态变更。</summary>
public sealed class StateChangedEventArgs(UpdateState oldState, UpdateState newState) : EventArgs
{
    public UpdateState OldState { get; } = oldState;

    public UpdateState NewState { get; } = newState;
}
