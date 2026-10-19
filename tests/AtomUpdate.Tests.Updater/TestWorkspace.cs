namespace AtomUpdate.Tests.Updater;

/// <summary>测试临时目录（每测试独立，Dispose 清理）。</summary>
public sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "atomupdate-tests", Guid.NewGuid().ToString("N"));

    public TestWorkspace() => Directory.CreateDirectory(Root);

    public string Resolve(params string[] parts) => Path.Combine([Root, .. parts]);

    public string CreateDirectory(params string[] parts)
    {
        var path = Resolve(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    public void WriteFile(string relative, string content)
    {
        var path = Resolve(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public string ReadFile(string relative) => File.ReadAllText(Resolve(relative.Split('/')));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception)
        {
            // 清理失败不影响测试结果
        }
    }
}
