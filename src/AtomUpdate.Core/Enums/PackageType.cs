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
    Rpm,

    /// <summary>Android APK 单文件（原样上传，不重新压缩，保签名有效）。</summary>
    Apk
}
