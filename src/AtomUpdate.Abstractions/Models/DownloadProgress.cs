namespace AtomUpdate.Abstractions.Models;

/// <summary>下载进度（04 §3.3 IProgress&lt;DownloadProgress&gt;）。</summary>
public sealed class DownloadProgress
{
    public required long BytesReceived { get; init; }

    /// <summary>总字节数（Content-Length；服务器未提供时为 null）。</summary>
    public long? TotalBytes { get; init; }

    /// <summary>0~100；TotalBytes 未知时为 null。</summary>
    public double? Percent { get; init; }

    /// <summary>瞬时速度（字节/秒）。</summary>
    public double SpeedBytesPerSecond { get; init; }

    /// <summary>剩余时间估算（速度或总大小未知时为 null）。</summary>
    public TimeSpan? RemainingTime { get; init; }
}
