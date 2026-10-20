namespace AtomUpdate.Updater.SelfUpdate;

/// <summary>更新器自升级清单（MVP 内部约定，独立分发契约二阶段再定）：GET manifestUrl 返回本结构。</summary>
public sealed class SelfUpdateManifest
{
    public string? Version { get; set; }

    public string? Url { get; set; }

    public string? Sha256 { get; set; }
}
