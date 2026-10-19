namespace AtomUpdate.Updater.Replace;

/// <summary>替换前登记的回滚清单（内存 + 落盘 backup-manifest.json 供诊断）。</summary>
public sealed class BackupManifest
{
    public string? BackupDirectory { get; set; }

    /// <summary>被覆盖/删除前已备份的文件（相对目标目录，'/' 分隔）。</summary>
    public List<string> BackedUpFiles { get; set; } = [];

    /// <summary>本次新创建的文件（回滚时删除）。</summary>
    public List<string> CreatedFiles { get; set; } = [];
}
