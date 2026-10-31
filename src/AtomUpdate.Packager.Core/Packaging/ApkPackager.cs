using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Core.Packaging;

/// <summary>APK 单文件打包（Android 侧载分发）：源目录取唯一的 <c>*.apk</c>，按
/// {projectKey}-{appId}-{version}-{platform}-{arch}.apk 复制到输出目录并计算 SHA-256。
/// **不重新压缩**——APK 本身是 zip 容器，重打包会破坏 v2/v3 签名，安装器会拒绝安装。</summary>
public sealed class ApkPackager : IPackager
{
    public bool Supports(PackageType packageType) => packageType == PackageType.Apk;

    public async Task<PackageResult> PackageAsync(PackageContext context,
        CancellationToken cancellationToken = default)
    {
        if (!Supports(context.PackageType))
        {
            throw new PackagerException(PackagerErrorCodes.UnsupportedPackageType,
                $"ApkPackager 不支持包类型：{context.PackageType}");
        }

        var sourceRoot = Path.GetFullPath(context.SourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new PackagerException(PackagerErrorCodes.SourceNotFound, $"发布目录不存在：{sourceRoot}");
        }

        var apks = Directory.EnumerateFiles(sourceRoot, "*.apk", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        if (apks.Count == 0)
        {
            throw new PackagerException(PackagerErrorCodes.SourceEmpty,
                $"发布目录未找到 .apk 文件：{sourceRoot}");
        }

        if (apks.Count > 1)
        {
            throw new PackagerException(PackagerErrorCodes.SourceEmpty,
                $"发布目录存在多个 .apk，请只保留本次要发布的一个：{string.Join(", ", apks.Select(Path.GetFileName))}");
        }

        var outputDirectory = ZipPackager.ResolveOutputDirectory(context);
        Directory.CreateDirectory(outputDirectory);
        var fileName = PackageNameBuilder.BuildFull(context);
        var packagePath = Path.Combine(outputDirectory, fileName);

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        // 逐字节复制：保持 APK 原始签名与对齐信息
        File.Copy(apks[0], packagePath);
        cancellationToken.ThrowIfCancellationRequested();

        return await ZipPackager.BuildResultAsync(packagePath, fileName, context, cancellationToken);
    }
}
