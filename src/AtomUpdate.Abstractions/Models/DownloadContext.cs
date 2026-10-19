using AtomUpdate.Core.Enums;

namespace AtomUpdate.Abstractions.Models;

/// <summary>下载上下文。</summary>
public sealed class DownloadContext
{
    public required UpdateInfo Update { get; init; }

    /// <summary>实际下载的包（已由门面按增量优先解析）。</summary>
    public required PackageAssetRef Package { get; init; }

    public DownloadMode Mode { get; init; }

    /// <summary>下载落盘目录（cache/{appId}/downloads/）。</summary>
    public required string DestinationDirectory { get; init; }

    /// <summary>限速（字节/秒；0 = 不限）。取本地配置与服务端策略的较小值。</summary>
    public long MaxSpeedBytesPerSecond { get; init; }

    /// <summary>无进展超时（秒；0 = 不启用）。</summary>
    public int StallTimeoutSeconds { get; init; }
}

/// <summary>包引用（从 PackageAsset 提炼下载所需最小字段，便于替换 IUpdateDownloader 时脱离 Core 模型）。</summary>
public sealed record PackageAssetRef
{
    public required string Url { get; init; }

    public required string Sha256 { get; init; }

    public required string PackageType { get; init; }

    public string? Signature { get; init; }

    public string? KeyId { get; init; }

    public long? Size { get; init; }
}
