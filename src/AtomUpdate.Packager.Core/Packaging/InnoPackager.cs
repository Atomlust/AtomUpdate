using System.Diagnostics;
using System.Text;
using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Core.Packaging;

/// <summary>Inno Setup exe 打包（docs/05 §6）：生成 .iss → ISCC 编译 → 单 exe 安装器。
/// ISCC 探测顺序：显式配置 → Program Files 常规路径 → PATH；缺失抛引导错误。</summary>
public sealed class InnoPackager : IPackager
{
    public bool Supports(PackageType packageType) => packageType == PackageType.Exe;

    public async Task<PackageResult> PackageAsync(PackageContext context, CancellationToken cancellationToken = default)
    {
        if (!Supports(context.PackageType))
        {
            throw new PackagerException(PackagerErrorCodes.UnsupportedPackageType,
                $"InnoPackager 不支持包类型：{context.PackageType}");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PackagerException(PackagerErrorCodes.UnsupportedPackageType,
                "Inno Setup 仅支持在 Windows 上打包 exe 安装器");
        }

        var sourceRoot = Path.GetFullPath(context.SourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new PackagerException(PackagerErrorCodes.SourceNotFound, $"发布目录不存在：{sourceRoot}");
        }

        var iscc = ResolveIscc(context.InnoSetupPath);
        if (iscc is null)
        {
            throw new PackagerException(PackagerErrorCodes.IsccNotFound,
                "未找到 Inno Setup 编译器（ISCC.exe）：请安装 Inno Setup 6，或通过 --inno-setup-path 指定 ISCC.exe 路径");
        }

        var outputDirectory = ZipPackager.ResolveOutputDirectory(context);
        Directory.CreateDirectory(outputDirectory);
        var fileName = PackageNameBuilder.BuildFull(context);
        var packagePath = Path.Combine(outputDirectory, fileName);
        var scriptPath = Path.Combine(outputDirectory, $"{fileName}.iss");
        var workDirectoryName = $"innosetup-{context.Arch.ToString().ToLowerInvariant()}";

        await File.WriteAllTextAsync(scriptPath, BuildScript(context, sourceRoot, outputDirectory, fileName),
            new UTF8Encoding(false), cancellationToken);

        var exitCode = await RunIsccAsync(iscc, scriptPath, cancellationToken);
        if (exitCode != 0 || !File.Exists(packagePath))
        {
            throw new PackagerException(PackagerErrorCodes.IsccFailed,
                $"ISCC 编译失败（exit {exitCode}）：{scriptPath}");
        }

        File.Delete(scriptPath);
        return await ZipPackager.BuildResultAsync(packagePath, fileName, context, cancellationToken);
    }

    internal static string BuildScript(PackageContext context, string sourceRoot, string outputDirectory,
        string fileName) => $@"; AtomUpdate Packager 自动生成
[Setup]
AppId={{{context.AppId}}}
AppName={context.AppId}
AppVersion={context.Version}
DefaultDirName={{autopf}}\{context.AppId}
DisableProgramGroupPage=yes
OutputDir={outputDirectory}
OutputBaseFilename={fileName}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible arm64
PrivilegesRequired=lowest

[Files]
Source: ""{sourceRoot}\*""; DestDir: ""{{app}}""; Flags: recursesubdirs createallsubdirs replacesameversion
";

    internal static string? ResolveIscc(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Inno Setup 6", "ISCC.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Inno Setup 6", "ISCC.exe")
        };
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null)
        {
            return found;
        }

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return pathDirs.Select(dir => Path.Combine(dir.Trim(), "ISCC.exe")).FirstOrDefault(File.Exists);
    }

    private static async Task<int> RunIsccAsync(string isccPath, string scriptPath,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = isccPath,
            Arguments = $"/Q \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new PackagerException(PackagerErrorCodes.IsccFailed, $"无法启动 ISCC：{isccPath}");

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
