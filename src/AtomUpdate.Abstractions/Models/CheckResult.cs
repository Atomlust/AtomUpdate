using AtomUpdate.Core.Models;

namespace AtomUpdate.Abstractions.Models;

/// <summary>检查结果：成功时携带更新信息与服务端策略。</summary>
public sealed class CheckResult
{
    public bool Success { get; private init; }

    /// <summary>服务端判定有可用更新。</summary>
    public bool HasUpdate { get; private init; }

    public UpdateInfo? Update { get; private init; }

    /// <summary>服务端本次下发的策略（可能为 null：304/无更新/策略未变）。</summary>
    public UpdatePolicy? ServerPolicy { get; private init; }

    public int? PolicyVersion { get; private init; }

    /// <summary>响应 ETag（供 IStateStore 持久化，下次请求 If-None-Match）。</summary>
    public string? ETag { get; private init; }

    public string? ErrorCode { get; private init; }

    public string? ErrorMessage { get; private init; }

    public Exception? Exception { get; private init; }

    public static CheckResult From(OperationResult<CheckPayload> result) => new()
    {
        Success = result.Success,
        HasUpdate = result.Data?.HasUpdate ?? false,
        Update = result.Data?.Update,
        ServerPolicy = result.Data?.ServerPolicy,
        PolicyVersion = result.Data?.PolicyVersion,
        ETag = result.Data?.ETag,
        ErrorCode = result.ErrorCode,
        ErrorMessage = result.ErrorMessage,
        Exception = result.Exception
    };
}

/// <summary>检查负载（UpdateInfo + 策略 + ETag 组合）。</summary>
public sealed class CheckPayload
{
    public bool HasUpdate { get; init; }

    public UpdateInfo? Update { get; init; }

    public UpdatePolicy? ServerPolicy { get; init; }

    public int? PolicyVersion { get; init; }

    public string? ETag { get; init; }
}
