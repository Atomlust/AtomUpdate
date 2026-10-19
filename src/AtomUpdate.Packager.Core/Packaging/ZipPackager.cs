using System.IO.Compression;
using System.Security.Cryptography;
using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Core.Packaging;

/// <summary>zip 全量打包（docs/05 §6）：相对路径条目、glob 排除、SHA256 小写 hex、
/// Unix 权限位（非 Windows 读真实 mode 写 ExternalAttributes）、跳过符号链接（与更新器 ZipInstaller 互为镜像）。</summary>
public sealed class ZipPackager : IPackager
{
    public bool Supports(PackageType packageType) => packageType == PackageType.Zip;

    public async Task<PackageResult> PackageAsync(PackageContext context, CancellationToken cancellationToken = default)
    {
        if (!Supports(context.PackageType))
        {
            throw new PackagerException(PackagerErrorCodes.UnsupportedPackageType,
                $"ZipPackager 不支持包类型：{context.PackageType}");
        }

        var sourceRoot = Path.GetFullPath(context.SourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new PackagerException(PackagerErrorCodes.SourceNotFound,
                $"发布目录不存在：{sourceRoot}");
        }

        var files = CollectFiles(sourceRoot, context.ExcludePatterns);
        if (files.Count == 0)
        {
            throw new PackagerException(PackagerErrorCodes.SourceEmpty,
                $"发布目录为空（或全部被排除）：{sourceRoot}");
        }

        var outputDirectory = ResolveOutputDirectory(context);
        Directory.CreateDirectory(outputDirectory);
        var fileName = PackageNameBuilder.BuildFull(context);
        var packagePath = Path.Combine(outputDirectory, fileName);

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        await using (var stream = File.Create(packagePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var (absolute, relative) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTimeUtc(absolute);
                await using var source = File.OpenRead(absolute);
                await using var target = entry.Open();
                await source.CopyToAsync(target, cancellationToken);

                ApplyUnixFileMode(entry, absolute);
            }
        }

        return await BuildResultAsync(packagePath, fileName, context, cancellationToken);
    }

    internal static List<(string Absolute, string Relative)> CollectFiles(string sourceRoot,
        IReadOnlyList<string> excludePatterns)
    {
        var files = new List<(string, string)>();
        foreach (var absolute in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(sourceRoot, absolute).Replace('\\', '/');
            if (new FileInfo(absolute).LinkTarget is not null)
            {
                continue; // 符号链接不入包（更新器拒绝符号链接条目）
            }

            if (GlobMatcher.IsExcluded(relative, excludePatterns))
            {
                continue;
            }

            files.Add((absolute, relative));
        }

        return files;
    }

    private static void ApplyUnixFileMode(ZipArchiveEntry entry, string sourcePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows 打包不写权限位；更新器按缺省处理
        }

        var mode = (uint)File.GetUnixFileMode(sourcePath) & 0xFFF;
        entry.ExternalAttributes = (int)(mode << 16);
    }

    internal static async Task<PackageResult> BuildResultAsync(string packagePath, string fileName,
        PackageContext context, CancellationToken cancellationToken)
    {
        var info = new FileInfo(packagePath);
        var sha256 = await PackageHasher.ComputeFileSha256Async(packagePath, cancellationToken);
        return new PackageResult
        {
            PackagePath = packagePath,
            FileName = fileName,
            Sha256 = sha256,
            Size = info.Length,
            PackageType = context.PackageType,
            Platform = context.Platform,
            Arch = context.Arch,
            SilentArgs = context.SilentArgs,
            InstallArgs = context.InstallArgs
        };
    }

    internal static string ResolveOutputDirectory(PackageContext context) =>
        context.OutputDirectory is { } output
            ? Path.GetFullPath(output)
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(context.SourceDirectory))!, "packs");
}

/// <summary>文件指纹工具。</summary>
public static class PackageHasher
{
    public static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
