namespace AtomUpdate.Packager.Core.Models;

/// <summary>上传结果（服务端 /upload 响应：下载 URL、SHA-256、大小、文件名）。</summary>
public sealed class UploadResult
{
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public required long Size { get; init; }
    public required string Filename { get; init; }
}

/// <summary>上传进度（GUI/CLI 展示）。</summary>
public sealed record UploadProgress(string FileName, long BytesTransferred, long TotalBytes)
{
    public double Percent => TotalBytes <= 0 ? 0 : Math.Round(BytesTransferred * 100.0 / TotalBytes, 1);
}

/// <summary>打包工具全局选项（CLI/GUI 公共）。</summary>
public sealed class PackagerOptions
{
    /// <summary>工作目录：drafts/ 草稿（packs 由打包输出目录决定）。</summary>
    public string WorkDirectory { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "atomupdate", "packager");

    public string? ApiUrl { get; init; }
    public string? ProjectKey { get; init; }
    public string? AppId { get; init; }
}
