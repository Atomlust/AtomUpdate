using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core.Packaging;

namespace AtomUpdate.Tests.Packager;

public class PackageNameBuilderTests
{
    [Theory]
    [InlineData(PackageType.Zip, ".zip")]
    [InlineData(PackageType.Exe, ".exe")]
    [InlineData(PackageType.Msi, ".msi")]
    [InlineData(PackageType.Dmg, ".dmg")]
    [InlineData(PackageType.Pkg, ".pkg")]
    [InlineData(PackageType.AppImage, ".AppImage")]
    [InlineData(PackageType.Deb, ".deb")]
    [InlineData(PackageType.Rpm, ".rpm")]
    public void ExtensionFor_AllPackageTypes(PackageType packageType, string expected)
    {
        Assert.Equal(expected, PackageNameBuilder.ExtensionFor(packageType));
    }

    [Fact]
    public void BuildFull_Lowercases_Platform_And_Arch()
    {
        var name = PackageNameBuilder.BuildFull("myproj", "com.example.app", "1.2.0",
            UpdatePlatform.Windows, UpdateArch.Arm64, PackageType.Zip);

        Assert.Equal("myproj-com.example.app-1.2.0-windows-arm64.zip", name);
    }

    [Fact]
    public void BuildDelta_Uses_Delta_Naming()
    {
        var name = PackageNameBuilder.BuildDelta("1.1.0", "1.2.0", UpdateArch.X64);

        Assert.Equal("delta-1.1.0-to-1.2.0-x64.zip", name);
    }
}
