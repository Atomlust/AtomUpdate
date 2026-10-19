using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Packager.Core.Api;

/// <summary>元数据构建（docs/05 §11）：多架构（full+delta）资产合并单条 ReleaseCreateRequest 防 409；
/// 四段日志全空则不下发 notes。</summary>
public static class MetaBuilder
{
    public static ReleaseCreateRequest BuildCreate(string version, IReadOnlyList<ReleaseAssetInputData> assets,
        ReleaseNoteInput? notes = null, bool forceUpdate = false, string? minSupportedVersion = null,
        ReleaseStatus? status = null, int? buildNumber = null)
    {
        if (assets.Count == 0)
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "至少需要一个资产才能创建版本");
        }

        foreach (var asset in assets)
        {
            ValidateAsset(asset);
        }

        return new ReleaseCreateRequest
        {
            Version = version,
            BuildNumber = buildNumber,
            Status = status,
            ForceUpdate = forceUpdate,
            MinSupportedVersion = minSupportedVersion,
            Assets = assets.Select(asset => asset.ToContract()).ToList(),
            Notes = NormalizeNotes(notes)
        };
    }

    public static ReleasePublishRequest BuildPublish(IReadOnlyList<UpdateChannel> channels,
        DateTimeOffset? publishedAt = null)
    {
        if (channels.Count == 0)
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "发布至少需要一个通道");
        }

        return new ReleasePublishRequest
        {
            Channels = channels.Distinct().OrderBy(channel => channel).ToList(),
            PublishedAt = publishedAt
        };
    }

    /// <summary>四段（new/improvements/fixes/notes）与 rawMarkdown 全空 → null（不下发 notes）。</summary>
    internal static ReleaseNoteInput? NormalizeNotes(ReleaseNoteInput? notes)
    {
        if (notes is null)
        {
            return null;
        }

        var hasSections = notes.New.Count > 0 || notes.Improvements.Count > 0 ||
            notes.Fixes.Count > 0 || notes.Notes.Count > 0 ||
            !string.IsNullOrWhiteSpace(notes.RawMarkdown);
        return hasSections ? notes : null;
    }

    private static void ValidateAsset(ReleaseAssetInputData asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Url) || string.IsNullOrWhiteSpace(asset.Sha256))
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "资产缺少 url/sha256");
        }

        if (asset.Kind == AssetKind.Delta)
        {
            if (string.IsNullOrWhiteSpace(asset.FromVersion))
            {
                throw new PackagerException(PackagerErrorCodes.ApiFailed, "delta 资产必须提供 fromVersion");
            }
        }
        else if (!string.IsNullOrWhiteSpace(asset.FromVersion))
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "full 资产不允许设置 fromVersion");
        }
    }
}
