namespace AtomUpdate.Abstractions.Exceptions;

/// <summary>SDK 异常基类（04 §3.15）。公开 API 倾向返回结果对象，异常保留给编程错误与不可恢复错误。</summary>
public class AtomUpdateException(string errorCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>错误码（ErrorCodes 常量）。</summary>
    public string ErrorCode { get; } = errorCode;
}

/// <summary>检查更新失败。</summary>
public sealed class UpdateCheckException(string errorCode, string message, Exception? innerException = null)
    : AtomUpdateException(errorCode, message, innerException);

/// <summary>下载失败。</summary>
public sealed class UpdateDownloadException(string errorCode, string message, Exception? innerException = null)
    : AtomUpdateException(errorCode, message, innerException);

/// <summary>安装调度失败。</summary>
public sealed class UpdateInstallException(string errorCode, string message, Exception? innerException = null)
    : AtomUpdateException(errorCode, message, innerException);

/// <summary>包校验失败（SHA256/签名）。</summary>
public sealed class UpdateVerifyException(string errorCode, string message, Exception? innerException = null)
    : AtomUpdateException(errorCode, message, innerException);
