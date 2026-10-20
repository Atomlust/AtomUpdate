using System.Text.Json;
using System.Text.Json.Serialization;
using AtomUpdate.Packager.Core;

namespace AtomUpdate.Packager.Models;

/// <summary>向导草稿（docs/05 §12）：drafts/{guid}.json，步骤切换时自动保存、启动时恢复最新。
/// 安全口径：任何密钥均不入草稿（登录密码仅存于当次内存）。</summary>
public sealed class WizardDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Step1 目标
    public string? ProjectId { get; set; }
    public string? AppId { get; set; }
    public string? ProjectKey { get; set; }
    public string? AppBusinessId { get; set; }
    public string Platform { get; set; } = "windows";
    public List<string> Channels { get; set; } = [];

    // Step2 打包
    public string? Version { get; set; }
    public string? BuildNumber { get; set; }
    public string? SourceDirectory { get; set; }
    public string PackageType { get; set; } = "zip";
    public List<string> Architectures { get; set; } = ["x64"];
    public bool EnableDelta { get; set; }
    public string? BaselineDirectory { get; set; }
    public string? FromVersion { get; set; }
    public List<string> ExcludePatterns { get; set; } = [];
    public string? OutputDirectory { get; set; }
    public string? SilentArgs { get; set; }
    public string? InstallArgs { get; set; }

    // Step3 日志
    public string NotesLanguage { get; set; } = "en";
    public List<string> NoteNew { get; set; } = [];
    public List<string> NoteImprovements { get; set; } = [];
    public List<string> NoteFixes { get; set; } = [];
    public List<string> NoteNotes { get; set; } = [];
    public string? NoteRawMarkdown { get; set; }

    // Step4 发布（无密钥；旧草稿中的 KeyFile/KeyId/CredentialId 字段反序列化时被忽略）
    public string Status { get; set; } = "draft";
    public bool ForceUpdate { get; set; }
    public string? MinSupportedVersion { get; set; }
    public bool PublishNow { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FileName => $"draft-{Id}.json";

    public void Save(PackagerPaths paths)
    {
        Directory.CreateDirectory(paths.DraftsDirectory);
        UpdatedAt = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(paths.DraftsDirectory, FileName),
            JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>恢复最新草稿；目录为空返回 null。</summary>
    public static WizardDraft? LoadLatest(PackagerPaths paths)
    {
        if (!Directory.Exists(paths.DraftsDirectory))
        {
            return null;
        }

        var latest = Directory.EnumerateFiles(paths.DraftsDirectory, "draft-*.json")
            .Select(file => new FileInfo(file))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        return latest is null ? null : JsonSerializer.Deserialize<WizardDraft>(File.ReadAllText(latest.FullName), JsonOptions);
    }
}
