namespace AtomUpdate.Updater.Replace;

/// <summary>增量包清单（05 §7：delta zip 内 manifest.json）。</summary>
public sealed class DeltaManifest
{
    public string? FromVersion { get; set; }

    public string? ToVersion { get; set; }

    public List<DeltaFileEntry> Files { get; set; } = [];

    public List<string> Deleted { get; set; } = [];
}

public sealed class DeltaFileEntry
{
    public string? Path { get; set; }

    public string? Hash { get; set; }

    public long Size { get; set; }
}
