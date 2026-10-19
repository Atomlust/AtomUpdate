using System.IO.Compression;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Packaging;

namespace AtomUpdate.Packager.Core.Delta;

/// <summary>目录对比增量生成器（docs/05 §7）：基线目录 vs 当前目录逐文件 SHA256 比较；
/// 新增/修改 → files/（保留相对路径）；删除 → manifest.deleted ∪ delete.json。
/// 产物为 delta zip：根含 manifest.json（camelCase）+ delete.json（字符串数组）+ files/，与更新器 ZipInstaller 互为镜像。</summary>
public sealed class FileDeltaGenerator
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public async Task<DeltaResult> GenerateAsync(DeltaContext context, CancellationToken cancellationToken = default)
    {
        var baselineRoot = Path.GetFullPath(context.BaselineDirectory);
        var currentRoot = Path.GetFullPath(context.CurrentDirectory);
        if (!Directory.Exists(baselineRoot))
        {
            throw new PackagerException(PackagerErrorCodes.DeltaBaselineInvalid,
                $"基线目录不存在：{baselineRoot}");
        }

        if (!Directory.Exists(currentRoot))
        {
            throw new PackagerException(PackagerErrorCodes.SourceNotFound,
                $"当前目录不存在：{currentRoot}");
        }

        var baseline = await SnapshotAsync(baselineRoot, context.ExcludePatterns, cancellationToken);
        var current = await SnapshotAsync(currentRoot, context.ExcludePatterns, cancellationToken);

        // 新增/修改：当前有、基线无或哈希不同
        var changed = new List<DeltaFileSnapshot>();
        foreach (var (relative, snapshot) in current)
        {
            if (!baseline.TryGetValue(relative, out var baselineHash) || baselineHash.Hash != snapshot.Hash)
            {
                changed.Add(snapshot);
            }
        }

        // 删除：基线有、当前无
        var deleted = baseline.Keys
            .Where(relative => !current.ContainsKey(relative))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToList();

        var outputDirectory = context.OutputDirectory is { } output
            ? Path.GetFullPath(output)
            : Path.Combine(Path.GetDirectoryName(currentRoot)!, "packs");
        Directory.CreateDirectory(outputDirectory);

        var fileName = PackageNameBuilder.BuildDelta(context);
        var packagePath = Path.Combine(outputDirectory, fileName);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        changed.Sort((a, b) => string.Compare(a.Relative, b.Relative, StringComparison.Ordinal));

        await using (var stream = File.Create(packagePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            // manifest.json（camelCase）+ delete.json 置于 zip 根（ZipInstaller 布局识别的依据）
            var manifestEntry = archive.CreateEntry(DeltaManifestFile.ManifestFileName, CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                await writer.WriteAsync(BuildManifestJson(context, changed, deleted));
            }

            var deleteEntry = archive.CreateEntry(DeltaManifestFile.DeleteFileName, CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(deleteEntry.Open()))
            {
                await writer.WriteAsync(DeltaManifestFile.SerializeDeleteList(deleted));
            }

            foreach (var file in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                archive.CreateEntryFromFile(file.Absolute, $"files/{file.Relative}", CompressionLevel.Optimal);
            }
        }

        var info = new FileInfo(packagePath);
        return new DeltaResult
        {
            PackagePath = packagePath,
            FileName = fileName,
            Sha256 = await PackageHasher.ComputeFileSha256Async(packagePath, cancellationToken),
            Size = info.Length,
            FromVersion = context.FromVersion,
            ToVersion = context.Version,
            Platform = context.Platform,
            Arch = context.Arch,
            FileCount = changed.Count,
            DeletedCount = deleted.Count
        };
    }

    internal static string BuildManifestJson(DeltaContext context, List<DeltaFileSnapshot> changed,
        List<string> deleted)
    {
        var manifest = new DeltaManifestPayload
        {
            FromVersion = context.FromVersion,
            ToVersion = context.Version
        };
        foreach (var file in changed)
        {
            manifest.Files.Add(new DeltaManifestFileEntry
            {
                Path = file.Relative,
                Hash = file.Hash,
                Size = file.Size
            });
        }

        manifest.Deleted.AddRange(deleted);
        return DeltaManifestFile.Serialize(manifest);
    }

    /// <summary>目录快照：相对路径 → 文件快照。跳过符号链接，应用排除模式。</summary>
    private static async Task<Dictionary<string, DeltaFileSnapshot>> SnapshotAsync(
        string root, IReadOnlyList<string> excludePatterns, CancellationToken cancellationToken)
    {
        var snapshot = new Dictionary<string, DeltaFileSnapshot>(StringComparer.FromComparison(PathComparison));
        foreach (var absolute in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(absolute).LinkTarget is not null)
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, absolute).Replace('\\', '/');
            if (GlobMatcher.IsExcluded(relative, excludePatterns))
            {
                continue;
            }

            var info = new FileInfo(absolute);
            snapshot[relative] = new DeltaFileSnapshot(
                absolute,
                relative,
                await PackageHasher.ComputeFileSha256Async(absolute, cancellationToken),
                info.Length);
        }

        return snapshot;
    }
}

/// <summary>增量文件快照（内部比较/清单构建用）。</summary>
internal sealed record DeltaFileSnapshot(string Absolute, string Relative, string Hash, long Size);
