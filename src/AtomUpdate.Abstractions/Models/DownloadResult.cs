using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;

namespace AtomUpdate.Abstractions.Models;

/// <summary>下载结果。</summary>
public sealed class DownloadResult
{
    public bool Success { get; private init; }

    /// <summary>校验通过的包文件路径。</summary>
    public string? PackagePath { get; private init; }

    /// <summary>本次落地的包字节数（含断点续传的已有部分），用于下载结果上报。</summary>
    public long Bytes { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public Exception? Exception { get; private init; }

    public static DownloadResult Ok(string packagePath, long bytes = 0) =>
        new() { Success = true, PackagePath = packagePath, Bytes = bytes };

    public static DownloadResult Fail(string errorCode, string errorMessage, Exception? exception = null) =>
        new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage, Exception = exception };

    public static DownloadResult Fail(Exception exception, string? errorCode = null, string? errorMessage = null) =>
        new()
        {
            Success = false,
            ErrorCode = errorCode ?? ErrorCodes.Unknown,
            ErrorMessage = errorMessage ?? exception.Message,
            Exception = exception
        };
}

/// <summary>检查+下载组合结果（CheckAndDownloadAsync）。</summary>
public sealed class CheckAndDownloadResult(CheckResult check, DownloadResult download)
{
    public CheckResult Check { get; } = check;

    public DownloadResult Download { get; } = download;

    public bool Success => Check.Success && (Check.HasUpdate ? Download.Success : true);
}

/// <summary>安装结果。</summary>
public sealed class InstallResult
{
    public bool Success { get; private init; }

    /// <summary>安装已调度（onRestart 模式下主程序退出后执行）。</summary>
    public bool Scheduled { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public Exception? Exception { get; private init; }

    public static InstallResult ScheduledOk() => new() { Success = true, Scheduled = true };

    public static InstallResult Ok() => new() { Success = true };

    public static InstallResult Fail(string errorCode, string errorMessage, Exception? exception = null) =>
        new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage, Exception = exception };
}
