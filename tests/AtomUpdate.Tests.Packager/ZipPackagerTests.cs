using System.IO.Compression;
using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Packaging;

namespace AtomUpdate.Tests.Packager;

public class ZipPackagerTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    [Fact]
    public async Task Package_CreatesZip_WithRelativeEntries_AndSha256()
    {
        _workspace.WriteFile("source/MyApp.exe", "binary");
        _workspace.WriteFile("source/config/appsettings.json", "{ \"a\": 1 }");
        _workspace.WriteFile("source/config/nested/deep.json", "{}");

        var result = await new ZipPackager().PackageAsync(Context());

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("myproj-com.example.app-1.2.0-windows-x64.zip", result.FileName);
        Assert.EndsWith("output/myproj-com.example.app-1.2.0-windows-x64.zip", result.PackagePath.Replace('\\', '/'));
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(result.Sha256, result.Sha256.ToLowerInvariant());

        using var archive = ZipFile.OpenRead(result.PackagePath);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).OrderBy(n => n).ToList();
        Assert.Equal(["config/appsettings.json", "config/nested/deep.json", "MyApp.exe"], names);
    }

    [Fact]
    public async Task Package_Sha256MatchesFile()
    {
        _workspace.WriteFile("source/a.txt", "hello");
        var result = await new ZipPackager().PackageAsync(Context());
        Assert.Equal(PackageHasher.ComputeFileSha256(result.PackagePath), result.Sha256);
        Assert.Equal(new FileInfo(result.PackagePath).Length, result.Size);
    }

    [Fact]
    public async Task Package_AppliesDefaultExcludes()
    {
        _workspace.WriteFile("source/app.dll", "x");
        _workspace.WriteFile("source/app.pdb", "y");
        _workspace.WriteFile("source/.git/HEAD", "ref");
        _workspace.WriteFile("source/src/.vs/cache", "v");
        _workspace.WriteFile("source/keep.txt", "keep");

        var result = await new ZipPackager().PackageAsync(Context());

        using var archive = ZipFile.OpenRead(result.PackagePath);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Equal(["app.dll", "keep.txt"], names);
    }

    [Fact]
    public async Task Package_AppliesCustomExcludePatterns()
    {
        _workspace.WriteFile("source/logs/trace.log", "z");
        _workspace.WriteFile("source/keep.txt", "keep");

        var context = Context(exclude: ["logs/**"]);
        var result = await new ZipPackager().PackageAsync(context);

        using var archive = ZipFile.OpenRead(result.PackagePath);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Equal(["keep.txt"], names);
    }

    [Fact]
    public async Task Package_EmptySource_Throws()
    {
        var error = await Assert.ThrowsAsync<PackagerException>(
            () => new ZipPackager().PackageAsync(Context()));
        Assert.Equal(PackagerErrorCodes.SourceEmpty, error.Code);
    }

    [Fact]
    public async Task Package_MissingSource_Throws()
    {
        var context = Context(source: _workspace.ToAbsolute("does-not-exist"));
        var error = await Assert.ThrowsAsync<PackagerException>(
            () => new ZipPackager().PackageAsync(context));
        Assert.Equal(PackagerErrorCodes.SourceNotFound, error.Code);
    }

    [Fact]
    public void Supports_OnlyZip()
    {
        Assert.True(new ZipPackager().Supports(PackageType.Zip));
        Assert.False(new ZipPackager().Supports(PackageType.Msi));
    }

    [Fact]
    public async Task Package_UnsupportedType_Throws()
    {
        var context = Context(packageType: PackageType.Msi);
        var error = await Assert.ThrowsAsync<PackagerException>(
            () => new ZipPackager().PackageAsync(context));
        Assert.Equal(PackagerErrorCodes.UnsupportedPackageType, error.Code);
    }

    private PackageContext Context(PackageType packageType = PackageType.Zip,
        string? source = null, IReadOnlyList<string>? exclude = null) => new()
    {
        ProjectKey = "myproj",
        AppId = "com.example.app",
        Version = "1.2.0",
        Platform = UpdatePlatform.Windows,
        Arch = UpdateArch.X64,
        PackageType = packageType,
        SourceDirectory = source ?? _workspace.SourceDirectory,
        OutputDirectory = _workspace.OutputDirectory,
        ExcludePatterns = exclude ?? PackageContext.DefaultExcludes
    };

    public void Dispose() => _workspace.Dispose();
}
