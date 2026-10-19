using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Core.Enums;
using AtomUpdate.Internal;

namespace AtomUpdate.Install;

/// <summary>内嵌更新器释放（04 §5.2/§6）：NuGet 包 runtimes/{rid}/native/ 下的 AOT 更新器 →
/// 版本化目录 updater/{version}/ + current 指针文件；版本化避免覆盖正在运行的更新器。</summary>
public static class UpdaterProvisioner
{
    /// <summary>可执行文件名（无扩展名；Windows 加 .exe）。</summary>
    public const string ExecutableName = "AtomUpdate.Updater";

    /// <summary>current 指针文件名，内容为版本目录名。</summary>
    public const string PointerFileName = "current";

    /// <summary>内嵌更新器在 SDK 包内的原生资产目录。</summary>
    public const string RuntimeAssetsFolder = "runtimes";

    /// <summary>解析可执行文件名的平台扩展名。</summary>
    public static string ExecutableWithExtension(bool windows) => windows ? ExecutableName + ".exe" : ExecutableName;

    /// <summary>平台/架构 → .NET RID（仅覆盖 6 个分发 RID；未知架构返回 null）。</summary>
    public static string? BuildRuntimeIdentifier(UpdatePlatform platform, UpdateArch arch)
    {
        var os = platform switch
        {
            UpdatePlatform.Windows => "win",
            UpdatePlatform.Macos => "osx",
            UpdatePlatform.Linux => "linux",
            _ => null
        };
        var architecture = arch switch
        {
            UpdateArch.X64 => "x64",
            UpdateArch.Arm64 => "arm64",
            UpdateArch.X86 => "x86",
            _ => null
        };
        return os is null || architecture is null ? null : $"{os}-{architecture}";
    }

    /// <summary>释放内嵌更新器到版本化目录并维护 current 指针；未随包分发/释放失败返回 null（调用方回落 UpdaterNotFound）。</summary>
    public static string? EnsureProvisioned(IPlatformInfo platformInfo, IUpdateLogger logger,
        string? sourceRoot = null, string? updaterRoot = null)
    {
        var rid = BuildRuntimeIdentifier(platformInfo.Platform, platformInfo.Arch);
        if (rid is null)
        {
            logger.Warning($"内嵌更新器未覆盖当前平台/架构：{platformInfo.Platform}/{platformInfo.Arch}");
            return null;
        }

        var source = LocateEmbedded(sourceRoot ?? AppContext.BaseDirectory, rid, platformInfo.RuntimeIdentifier);
        if (source is null)
        {
            return null;
        }

        var root = updaterRoot ?? PlatformPaths.AtomUpdateRoot;
        var version = VersionDirectoryName();
        var destinationDirectory = Path.Combine(root, "updater", version);
        var destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
        var pointer = Path.Combine(root, "updater", PointerFileName);

        try
        {
            var currentVersion = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : null;
            var upToDate = string.Equals(currentVersion, version, StringComparison.Ordinal)
                && File.Exists(destination)
                && new FileInfo(destination).Length == new FileInfo(source).Length;
            if (!upToDate)
            {
                Directory.CreateDirectory(destinationDirectory);
                var staging = destination + ".tmp";
                File.Copy(source, staging, overwrite: true);
                File.Move(staging, destination, overwrite: true);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(destination,
                        File.GetUnixFileMode(source) | UnixFileMode.UserExecute);
                }

                logger.Information($"更新器已释放：{destination}");
            }

            File.WriteAllText(pointer, version);
            return destination;
        }
        catch (Exception exception)
        {
            logger.Error($"更新器释放失败：{source} → {destination}", exception);
            return null;
        }
    }

    /// <summary>定位内嵌更新器：优先 NuGet 原生资产（runtimes/{rid}/native/），其次输出根（RID 专项发布扁平化）。</summary>
    public static string? LocateEmbedded(string baseDirectory, string rid, string? runtimeIdentifier = null)
    {
        var fileName = ExecutableWithExtension(OperatingSystem.IsWindows());
        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, RuntimeAssetsFolder, rid, "native", fileName)
        };

        // RuntimeInformation 可能给出更特定的 RID（如 linux-musl-x64）：其目录名同样探查
        if (!string.IsNullOrEmpty(runtimeIdentifier) && !string.Equals(runtimeIdentifier, rid, StringComparison.Ordinal))
        {
            candidates.Add(Path.Combine(baseDirectory, RuntimeAssetsFolder, runtimeIdentifier, "native", fileName));
        }

        candidates.Add(Path.Combine(baseDirectory, fileName));
        candidates.Add(Path.Combine(baseDirectory, "updater", rid, fileName));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>版本目录名：SDK 程序集版本（三段式，如 0.1.0）。</summary>
    public static string VersionDirectoryName() =>
        typeof(UpdaterProvisioner).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
