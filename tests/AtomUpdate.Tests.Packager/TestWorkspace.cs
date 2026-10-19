namespace AtomUpdate.Tests.Packager;

/// <summary>测试临时工作区：每个用例独立的源目录/基线目录/输出目录，Dispose 清理。</summary>
public sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "atomupdate-tests", Guid.NewGuid().ToString("N"));

    public string SourceDirectory { get; } = default!;

    /// <summary>增量"当前目录"（与 BaselineDirectory 对仗；全量打包用 SourceDirectory）。</summary>
    public string CurrentDirectory { get; } = default!;

    public string BaselineDirectory { get; } = default!;

    public string OutputDirectory { get; } = default!;

    public TestWorkspace()
    {
        Directory.CreateDirectory(Root);
        SourceDirectory = Path.Combine(Root, "source");
        CurrentDirectory = Path.Combine(Root, "current");
        BaselineDirectory = Path.Combine(Root, "baseline");
        OutputDirectory = Path.Combine(Root, "output");
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(CurrentDirectory);
        Directory.CreateDirectory(BaselineDirectory);
        Directory.CreateDirectory(OutputDirectory);
    }

    /// <summary>在工作区内写文件（相对路径，正反斜杠均可）。</summary>
    public void WriteFile(string relativePath, string content)
    {
        var path = ToAbsolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void WriteFile(string relativePath, byte[] content)
    {
        var path = ToAbsolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public string ReadFile(string relativePath) => File.ReadAllText(ToAbsolute(relativePath));

    public string ToAbsolute(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
