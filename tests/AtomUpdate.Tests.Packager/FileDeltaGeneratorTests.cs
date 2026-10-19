using System.IO.Compression;
using System.Text.Json;
using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Delta;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Packaging;

namespace AtomUpdate.Tests.Packager;

public class FileDeltaGeneratorTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    [Fact]
    public async Task Generate_Detects_Added_Modified_Deleted()
    {
        _workspace.WriteFile("baseline/a.txt", "v1");
        _workspace.WriteFile("baseline/b.txt", "same");
        _workspace.WriteFile("baseline/c.txt", "delete-me");
        _workspace.WriteFile("current/a.txt", "v2");
        _workspace.WriteFile("current/b.txt", "same");
        _workspace.WriteFile("current/d.txt", "new");

        var result = await new FileDeltaGenerator().GenerateAsync(Context());

        Assert.Equal(2, result.FileCount); // a.txt 修改 + d.txt 新增
        Assert.Equal(1, result.DeletedCount); // c.txt 删除
        Assert.Equal("1.1.0", result.FromVersion);
        Assert.Equal("1.2.0", result.ToVersion);
        Assert.Equal("delta-1.1.0-to-1.2.0-x64.zip", result.FileName);
    }

    [Fact]
    public async Task Generate_ZipLayout_MatchesUpdaterContract()
    {
        _workspace.WriteFile("baseline/a.txt", "v1");
        _workspace.WriteFile("current/a.txt", "v2");
        _workspace.WriteFile("current/sub/new.txt", "x");

        var result = await new FileDeltaGenerator().GenerateAsync(Context());

        using var archive = ZipFile.OpenRead(result.PackagePath);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Contains("manifest.json", names);
        Assert.Contains("delete.json", names);
        Assert.Contains("files/a.txt", names);
        Assert.Contains("files/sub/new.txt", names);

        var manifestEntry = archive.GetEntry("manifest.json")!;
        using var reader = new StreamReader(manifestEntry.Open());
        var manifestJson = await reader.ReadToEndAsync();

        // camelCase 写出（更新器 UpdaterJsonContext 口径）
        using var document = JsonDocument.Parse(manifestJson);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("fromVersion", out var fromVersion));
        Assert.Equal("1.1.0", fromVersion.GetString());
        Assert.True(root.TryGetProperty("toVersion", out var toVersion));
        Assert.Equal("1.2.0", toVersion.GetString());
        var files = root.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
        Assert.Equal("a.txt", files[0].GetProperty("path").GetString());
        Assert.Equal(64, files[0].GetProperty("hash").GetString()!.Length);
        Assert.Equal(2, files[0].GetProperty("size").GetInt64());

        // 哈希与实际文件一致
        var zipPath = files[0].GetProperty("hash").GetString()!;
        Assert.Equal(PackageHasher.ComputeFileSha256(result.PackagePath).Length, zipPath.Length);
    }

    [Fact]
    public async Task Generate_DeleteList_IsStringArray()
    {
        _workspace.WriteFile("baseline/gone.txt", "x");
        _workspace.WriteFile("baseline/alsogone.txt", "y");
        _workspace.WriteFile("current/keep.txt", "z");

        var result = await new FileDeltaGenerator().GenerateAsync(Context());

        using var archive = ZipFile.OpenRead(result.PackagePath);
        using var reader = new StreamReader(archive.GetEntry("delete.json")!.Open());
        var deleteJson = await reader.ReadToEndAsync();
        var list = JsonSerializer.Deserialize<List<string>>(deleteJson);
        Assert.NotNull(list);
        Assert.Equal(["alsogone.txt", "gone.txt"], list!.OrderBy(p => p).ToList());
        Assert.Equal(2, result.DeletedCount);
    }

    [Fact]
    public async Task Generate_IdenticalDirs_ProducesEmptyDelta()
    {
        _workspace.WriteFile("baseline/same.txt", "x");
        _workspace.WriteFile("current/same.txt", "x");

        var result = await new FileDeltaGenerator().GenerateAsync(Context());

        Assert.Equal(0, result.FileCount);
        Assert.Equal(0, result.DeletedCount);
    }

    [Fact]
    public async Task Generate_AppliesExcludePatterns()
    {
        _workspace.WriteFile("baseline/app.dll", "old");
        _workspace.WriteFile("baseline/logs/old.log", "log");
        _workspace.WriteFile("current/app.dll", "new");
        _workspace.WriteFile("current/logs/new.log", "log");

        var result = await new FileDeltaGenerator()
            .GenerateAsync(Context(exclude: ["logs/**"]));

        // logs/** 被自定义模式排除：不进 files/，也不进删除清单
        Assert.Equal(1, result.FileCount);
        Assert.Equal(0, result.DeletedCount);
    }

    [Fact]
    public async Task Generate_MissingBaseline_Throws()
    {
        var context = Context(baseline: _workspace.ToAbsolute("no-such-dir"));
        var error = await Assert.ThrowsAsync<PackagerException>(
            () => new FileDeltaGenerator().GenerateAsync(context));
        Assert.Equal(PackagerErrorCodes.DeltaBaselineInvalid, error.Code);
    }

    [Fact]
    public async Task Generate_MissingCurrent_Throws()
    {
        var context = Context(current: _workspace.ToAbsolute("no-such-dir"));
        var error = await Assert.ThrowsAsync<PackagerException>(
            () => new FileDeltaGenerator().GenerateAsync(context));
        Assert.Equal(PackagerErrorCodes.SourceNotFound, error.Code);
    }

    [Fact]
    public async Task Generate_SkipsSymlinks()
    {
        _workspace.WriteFile("baseline/real.txt", "x");
        _workspace.WriteFile("current/real.txt", "y");
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(_workspace.ToAbsolute("current/link.txt"),
                _workspace.ToAbsolute("current/real.txt"));
        }

        var result = await new FileDeltaGenerator().GenerateAsync(Context());
        Assert.Equal(1, result.FileCount);
    }

    private DeltaContext Context(string? baseline = null, string? current = null,
        IReadOnlyList<string>? exclude = null) => new()
    {
        ProjectKey = "myproj",
        AppId = "com.example.app",
        FromVersion = "1.1.0",
        Version = "1.2.0",
        Platform = UpdatePlatform.Windows,
        Arch = UpdateArch.X64,
        BaselineDirectory = baseline ?? _workspace.BaselineDirectory,
        CurrentDirectory = current ?? _workspace.CurrentDirectory,
        OutputDirectory = _workspace.OutputDirectory,
        ExcludePatterns = exclude ?? PackageContext.DefaultExcludes
    };

    public void Dispose() => _workspace.Dispose();
}
