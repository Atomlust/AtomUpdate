using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtomUpdate.Packager.Core.Delta;

/// <summary>增量清单（docs/05 §7）：delta zip 内 manifest.json + delete.json，
/// 结构与更新器 DeltaManifest（UpdaterJsonContext camelCase）互为镜像；此处独立序列化，不引用 Updater 项目。</summary>
public static class DeltaManifestFile
{
    public const string ManifestFileName = "manifest.json";

    public const string DeleteFileName = "delete.json";

    /// <summary>camelCase 写出（与 Updater/Json 的 JsonKnownNamingPolicy.CamelCase 实证口径一致）。</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static DeltaManifestPayload ReadManifest(string path) =>
        JsonSerializer.Deserialize<DeltaManifestPayload>(File.ReadAllText(path), Options)
        ?? throw new PackagerException(PackagerErrorCodes.DeltaBaselineInvalid,
            $"增量清单解析为空：{path}");

    public static string Serialize(DeltaManifestPayload manifest) =>
        JsonSerializer.Serialize(manifest, Options);

    public static string SerializeDeleteList(IReadOnlyList<string> deleted) =>
        JsonSerializer.Serialize(deleted, Options);
}

public sealed class DeltaManifestPayload
{
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }
    public List<DeltaManifestFileEntry> Files { get; set; } = [];
    public List<string> Deleted { get; set; } = [];
}

public sealed class DeltaManifestFileEntry
{
    public string? Path { get; set; }
    public string? Hash { get; set; }
    public long Size { get; set; }
}
