using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Core.Enums;
using AtomUpdate.Internal;

namespace AtomUpdate.Platform;

/// <summary>跨平台平台探测（04 §3.11；WMI/电源/网络精细化归二阶段）。</summary>
public sealed class PlatformInfo : IPlatformInfo
{
    public UpdatePlatform Platform { get; }

    public UpdateArch Arch { get; }

    public string OsVersion { get; }

    public string RuntimeIdentifier { get; }

    public PlatformInfo()
    {
        Platform = OperatingSystem.IsWindows() ? UpdatePlatform.Windows
            : OperatingSystem.IsMacOS() ? UpdatePlatform.Macos
            : UpdatePlatform.Linux;

        Arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => UpdateArch.Arm64,
            _ => UpdateArch.X64
        };

        OsVersion = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        RuntimeIdentifier = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
    }
}
