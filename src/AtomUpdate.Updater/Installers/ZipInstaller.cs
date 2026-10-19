using System.IO.Compression;
using System.Text.Json;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Json;
using AtomUpdate.Updater.Logging;
using AtomUpdate.Updater.Replace;
using AtomUpdate.Updater.Verification;

namespace AtomUpdate.Updater.Installers;

/// <summary>zip 安装器（04 §4.7/§4.9，05 §7）：解压临时目录（zip-slip / 符号链接防护）→ 识别 full/delta →
/// delta 校验 fromVersion 与逐文件哈希/大小 → 替换引擎（含备份/删除清单）→ 清理临时目录。</summary>
public sealed class ZipInstaller(UpdaterTaskFile task, UpdaterLog log) : IPackageInstaller
{
    private const string ManifestFileName = "manifest.json";

    private const string DeleteFileName = "delete.json";

    public Task<BackupManifest?> InstallAsync(string targetRoot, CancellationToken ct)
    {
        var extractDirectory = Path.Combine(Path.GetTempPath(), "atomupdate", $"extract-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(extractDirectory);
            Extract(extractDirectory, ct);
            var (sourceRoot, deletions) = ResolveLayout(extractDirectory);
            var manifest = new FileReplacer(task, log).Apply(sourceRoot, targetRoot, deletions);
            return Task.FromResult<BackupManifest?>(manifest);
        }
        finally
        {
            DeleteDirectory(extractDirectory);
        }
    }

    /// <summary>解压 + 安全防护：条目路径必须落在解压根内（zip-slip）；拒绝符号链接条目；Unix 下恢复权限位。</summary>
    private void Extract(string extractDirectory, CancellationToken ct)
    {
        var extractRoot = Path.GetFullPath(extractDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        using var archive = ZipFile.OpenRead(task.PackagePath!);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var relative = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (relative.Length == 0)
            {
                continue;
            }

            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new UpdaterFailureException(ExitCodes.InstallerFailed,
                    $"更新包包含符号链接条目（已拒绝）：{entry.FullName}");
            }

            var destination = Path.GetFullPath(Path.Combine(extractRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(extractRoot + Path.DirectorySeparatorChar, comparison) &&
                !string.Equals(destination, extractRoot, comparison))
            {
                throw new UpdaterFailureException(ExitCodes.InstallerFailed, $"更新包路径越界（zip-slip）：{entry.FullName}");
            }

            if (relative.EndsWith('/') || entry.Name.Length == 0)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);

            if (!OperatingSystem.IsWindows())
            {
                var unixMode = (entry.ExternalAttributes >> 16) & 0xFFF;
                if (unixMode != 0)
                {
                    File.SetUnixFileMode(destination, (UnixFileMode)unixMode);
                }
            }
        }
    }

    /// <summary>布局识别：显式 kind 优先；否则 delete.json + manifest.json 同时存在视为 delta（05 §7）。
    /// delta → 源为 files/、删除清单为 delete.json ∪ manifest.deleted。</summary>
    private (string SourceRoot, IReadOnlyList<string>? Deletions) ResolveLayout(string extractDirectory)
    {
        var manifestPath = Path.Combine(extractDirectory, ManifestFileName);
        var deletePath = Path.Combine(extractDirectory, DeleteFileName);
        var explicitKind = task.Kind?.ToLowerInvariant();

        var isDelta = explicitKind switch
        {
            "delta" => true,
            "full" => false,
            _ => File.Exists(manifestPath) && File.Exists(deletePath)
        };

        if (!isDelta)
        {
            log.Debug("按全量包处理（zip 根 = 目标目录）");
            return (extractDirectory, null);
        }

        if (!File.Exists(manifestPath))
        {
            throw new UpdaterFailureException(ExitCodes.InstallerFailed, "增量包缺少 manifest.json（无法校验基线版本）");
        }

        DeltaManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), UpdaterJsonContext.Default.DeltaManifest)
                       ?? throw new UpdaterFailureException(ExitCodes.InstallerFailed, "增量包 manifest.json 解析为空");
        }
        catch (JsonException exception)
        {
            throw new UpdaterFailureException(ExitCodes.InstallerFailed,
                $"增量包 manifest.json 解析失败：{exception.Message}", exception);
        }

        if (!string.IsNullOrWhiteSpace(manifest.FromVersion) &&
            !string.Equals(manifest.FromVersion, task.FromVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdaterFailureException(ExitCodes.VerificationFailed,
                $"增量包基线不匹配：包 fromVersion={manifest.FromVersion}，当前版本={task.FromVersion}");
        }

        var filesRoot = Path.Combine(extractDirectory, "files");
        if (!Directory.Exists(filesRoot))
        {
            throw new UpdaterFailureException(ExitCodes.InstallerFailed, "增量包缺少 files/ 目录");
        }

        VerifyDeltaFiles(filesRoot, manifest);

        var deletions = new List<string>(manifest.Deleted);
        if (File.Exists(deletePath))
        {
            try
            {
                var list = JsonSerializer.Deserialize(File.ReadAllText(deletePath), UpdaterJsonContext.Default.ListString);
                if (list is not null)
                {
                    deletions.AddRange(list);
                }
            }
            catch (JsonException exception)
            {
                throw new UpdaterFailureException(ExitCodes.InstallerFailed,
                    $"增量包 delete.json 解析失败：{exception.Message}", exception);
            }
        }

        log.Information($"增量包：from={manifest.FromVersion}→{manifest.ToVersion}，文件 {manifest.Files.Count} 个，删除 {deletions.Count} 项");
        return (filesRoot, deletions);
    }

    private void VerifyDeltaFiles(string filesRoot, DeltaManifest manifest)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var normalizedRoot = Path.GetFullPath(filesRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var entry in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(filesRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
            {
                throw new UpdaterFailureException(ExitCodes.InstallerFailed, $"增量清单路径越界：{entry.Path}");
            }

            if (!File.Exists(path))
            {
                throw new UpdaterFailureException(ExitCodes.VerificationFailed, $"增量包缺少清单文件：{entry.Path}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Hash))
            {
                var actual = PackageVerifier.ComputeSha256(path);
                if (!string.Equals(actual, entry.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdaterFailureException(ExitCodes.VerificationFailed,
                        $"增量包文件哈希不匹配：{entry.Path}（期望 {entry.Hash}，实际 {actual}）");
                }
            }

            if (entry.Size > 0)
            {
                var actualSize = new FileInfo(path).Length;
                if (actualSize != entry.Size)
                {
                    throw new UpdaterFailureException(ExitCodes.VerificationFailed,
                        $"增量包文件大小不匹配：{entry.Path}（期望 {entry.Size}，实际 {actualSize}）");
                }
            }
        }
    }

    private void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception)
        {
            log.Debug($"清理解压目录失败（不影响安装）：{exception.Message}");
        }
    }
}
