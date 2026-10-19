using AtomUpdate.Core.Constants;

namespace AtomUpdate.Abstractions.Models;

/// <summary>操作结果模型（04 §3.15）：公开 API 返回结果对象而非抛异常。</summary>
public sealed class OperationResult<T>
{
    public bool Success { get; private init; }

    public T? Data { get; private init; }

    /// <summary>失败时的错误码（ErrorCodes 常量）。</summary>
    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public Exception? Exception { get; private init; }

    public static OperationResult<T> Ok(T data) => new() { Success = true, Data = data };

    public static OperationResult<T> Fail(string errorCode, string errorMessage, Exception? exception = null) =>
        new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage, Exception = exception };

    /// <summary>由异常推导错误码（HttpRequestException→NETWORK_ERROR，其余→UNKNOWN）。</summary>
    public static OperationResult<T> Fail(Exception exception, string? errorCode = null, string? errorMessage = null) =>
        new()
        {
            Success = false,
            ErrorCode = errorCode ?? Classify(exception),
            ErrorMessage = errorMessage ?? exception.Message,
            Exception = exception
        };

    public static string Classify(Exception exception) => exception switch
    {
        HttpRequestException or TaskCanceledException => ErrorCodes.NetworkError,
        _ => ErrorCodes.Unknown
    };
}
