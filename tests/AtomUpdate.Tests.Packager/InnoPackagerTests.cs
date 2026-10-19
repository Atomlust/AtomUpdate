using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Packaging;

namespace AtomUpdate.Tests.Packager;

public class InnoPackagerTests
{
    [Fact]
    public void Supports_OnlyExe()
    {
        Assert.True(new InnoPackager().Supports(PackageType.Exe));
        Assert.False(new InnoPackager().Supports(PackageType.Zip));
    }

    [Fact]
    public void BuildScript_ContainsInnoSections()
    {
        var context = new PackageContext
        {
            ProjectKey = "proj",
            AppId = "com.example.app",
            Version = "2.0.0",
            Platform = UpdatePlatform.Windows,
            Arch = UpdateArch.X64,
            PackageType = PackageType.Exe,
            SourceDirectory = @"C:\build\publish"
        };

        var script = InnoPackager.BuildScript(context, @"C:\build\publish", @"C:\build\packs",
            "proj-com.example.app-2.0.0-windows-x64.exe");

        Assert.Contains("[Setup]", script);
        Assert.Contains("[Files]", script);
        Assert.Contains("AppId={com.example.app}", script);
        Assert.Contains("AppVersion=2.0.0", script);
        Assert.Contains("DefaultDirName={autopf}\\com.example.app", script);
        Assert.Contains(@"Source: ""C:\build\publish\*""", script);
        Assert.Contains(@"DestDir: ""{app}""", script);
        Assert.Contains("recursesubdirs", script);
    }

    [Fact]
    public void ResolveIscc_ExplicitMissingPath_ReturnsNull()
    {
        Assert.Null(InnoPackager.ResolveIscc(@"C:\definitely-not-here\ISCC.exe"));
    }

    [Fact]
    public async Task Package_NonWindows_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // 仅非 Windows 抛错
        }

        var workspace = new TestWorkspace();
        try
        {
            var context = new PackageContext
            {
                ProjectKey = "proj",
                AppId = "app",
                Version = "1.0.0",
                Platform = UpdatePlatform.Windows,
                Arch = UpdateArch.X64,
                PackageType = PackageType.Exe,
                SourceDirectory = workspace.SourceDirectory
            };

            var error = await Assert.ThrowsAsync<PackagerException>(
                () => new InnoPackager().PackageAsync(context));
            Assert.Equal(PackagerErrorCodes.UnsupportedPackageType, error.Code);
        }
        finally
        {
            workspace.Dispose();
        }
    }
}
