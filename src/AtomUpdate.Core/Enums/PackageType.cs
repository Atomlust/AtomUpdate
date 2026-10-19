using System.Text.Json.Serialization;

namespace AtomUpdate.Core.Enums;

public enum PackageType
{
    Zip,
    Exe,
    Msi,
    Dmg,
    Pkg,
    [JsonStringEnumMemberName("appimage")]
    AppImage,
    Deb,
    Rpm
}
