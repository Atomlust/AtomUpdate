using AtomUpdate.Updater;

namespace AtomUpdate.Tests.Updater;

/// <summary>启动配置清理（04 §5.1）：只删本组件写入的临时文件，用户手写的 --config 保留。</summary>
public sealed class LaunchConfigCleanupTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "atomupdate");

    private readonly string _probeFolder;

    public LaunchConfigCleanupTests()
    {
        // 样本一律放 %Temp%\atomupdate 下的独立子目录，验证名称约定而非位置巧合
        _probeFolder = Path.Combine(_tempRoot, "cleanup-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_probeFolder);
    }

    private string Write(string fileName)
    {
        var path = Path.Combine(_probeFolder, fileName);
        File.WriteAllText(path, "{}");
        return path;
    }

    [Fact]
    public void SdkTempConfig_IsDeleted()
    {
        var path = Write("updater-" + Guid.NewGuid().ToString("N") + ".json");

        Assert.True(UpdaterApp.TryDeleteLaunchConfig(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SelfUpdateHandoff_IsDeleted()
    {
        var path = Path.Combine(_tempRoot, "selfupdate", "handoff-" + Guid.NewGuid().ToString("N") + ".json");
        var createdFolder = !Directory.Exists(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");

        Assert.True(UpdaterApp.TryDeleteLaunchConfig(path));
        Assert.False(File.Exists(path));
        if (createdFolder)
        {
            Directory.Delete(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void UnconventionalName_IsKept()
    {
        var path = Write("my-task.json");

        Assert.False(UpdaterApp.TryDeleteLaunchConfig(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ConventionNameOutsideTempRoot_IsKept()
    {
        // 约定目录是 %Temp%\atomupdate，同名文件放在别处属于用户文件
        var path = Path.Combine(Path.GetTempPath(), "atomupdate-outside-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{}");
        try
        {
            Assert.False(UpdaterApp.TryDeleteLaunchConfig(path));
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HandoffOutsideSelfUpdateFolder_IsKept()
    {
        var path = Write("handoff-abc.json");

        Assert.False(UpdaterApp.TryDeleteLaunchConfig(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void MissingFileOrEmptyPath_ReturnsFalse()
    {
        // 提权回环：子进程已删，父进程再删应静默跳过
        Assert.False(UpdaterApp.TryDeleteLaunchConfig(
            Path.Combine(_tempRoot, "updater-gone-" + Guid.NewGuid().ToString("N") + ".json")));
        Assert.False(UpdaterApp.TryDeleteLaunchConfig(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_probeFolder))
        {
            Directory.Delete(_probeFolder, recursive: true);
        }
    }
}
