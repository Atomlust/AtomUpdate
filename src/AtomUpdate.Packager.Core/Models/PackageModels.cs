using AtomUpdate.Core.Enums;

namespace AtomUpdate.Packager.Core.Models;

/// <summary>打包上下文（docs/05 §6）：发布目录 + 版本/平台/架构 → 包文件。</summary>
public sealed class PackageContext
{
    public required string ProjectKey { get; init; }
    public required string AppId { get; init; }
    public required string Version { get; init; }
    public required UpdatePlatform Platform { get; init; }
    public required UpdateArch Arch { get; init; }
    public required PackageType PackageType { get; init; }

    /// <summary>发布目录（dotnet publish / 构建产物根）。</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>输出目录；缺省为发布目录同级 packs/。</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>排除模式（glob，相对路径）；缺省排除常见 VCS/调试产物。</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = DefaultExcludes;

    /// <summary>exe/msi 静默参数（随资产下发）。</summary>
    public string? SilentArgs { get; init; }

    /// <summary>exe/msi 附加安装参数（随资产下发）。</summary>
    public string? InstallArgs { get; init; }

    /// <summary>Inno Setup 编译器 ISCC.exe 路径（打包 exe 时优先探测）。</summary>
    public string? InnoSetupPath { get; init; }

    public static readonly IReadOnlyList<string> DefaultExcludes =
    [
        ".git", ".git/**", "**/.git/**",
        ".vs", ".vs/**", "**/.vs/**",
        "*.pdb", "**/*.pdb",
        "*.md", "**/*.md",
        ".DS_Store", "**/.DS_Store",
        "Thumbs.db", "**/Thumbs.db"
    ];
}

/// <summary>打包结果：包文件 + 指纹（SHA256 小写 hex）。</summary>
public sealed class PackageResult
{
    public required string PackagePath { get; init; }
    public required string FileName { get; init; }
    public required string Sha256 { get; init; }
    public required long Size { get; init; }
    public required PackageType PackageType { get; init; }
    public required UpdatePlatform Platform { get; init; }
    public required UpdateArch Arch { get; init; }
    public string? SilentArgs { get; init; }
    public string? InstallArgs { get; init; }
}

/// <summary>增量包上下文（docs/05 §7）：基线目录 vs 当前目录 → delta zip。</summary>
public sealed class DeltaContext
{
    public required string ProjectKey { get; init; }
    public required string AppId { get; init; }
    public required string FromVersion { get; init; }
    public required string Version { get; init; }
    public required UpdatePlatform Platform { get; init; }
    public required UpdateArch Arch { get; init; }

    /// <summary>基线目录（上一版本发布目录）。</summary>
    public required string BaselineDirectory { get; init; }

    /// <summary>当前目录（新版本发布目录）。</summary>
    public required string CurrentDirectory { get; init; }

    public string? OutputDirectory { get; init; }
    public IReadOnlyList<string> ExcludePatterns { get; init; } = PackageContext.DefaultExcludes;
}

/// <summary>增量包结果。</summary>
public sealed class DeltaResult
{
    public required string PackagePath { get; init; }
    public required string FileName { get; init; }
    public required string Sha256 { get; init; }
    public required long Size { get; init; }
    public required string FromVersion { get; init; }
    public required string ToVersion { get; init; }
    public required UpdatePlatform Platform { get; init; }
    public required UpdateArch Arch { get; init; }
    public required int FileCount { get; init; }
    public required int DeletedCount { get; init; }
}


