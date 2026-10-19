using AtomUpdate.Updater.Locking;
using AtomUpdate.Updater.State;

namespace AtomUpdate.Tests.Updater;

public class StateStoreTests
{
    [Fact]
    public void SaveAndLoad_Roundtrip()
    {
        using var workspace = new TestWorkspace();
        var store = new StateStore("demo", workspace.Resolve("state"));
        var state = new UpdaterState
        {
            AppId = "demo",
            ProjectKey = "proj",
            FromVersion = "1.0.0",
            ToVersion = "1.0.1",
            Stage = StateStages.Completed,
            Result = StateResults.Success,
            Reported = false,
            RetryCount = 2,
            UpdaterVersion = "0.1.0"
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("demo", loaded!.AppId);
        Assert.Equal("1.0.1", loaded.ToVersion);
        Assert.Equal(StateStages.Completed, loaded.Stage);
        Assert.Equal(StateResults.Success, loaded.Result);
        Assert.False(loaded.Reported);
        Assert.Equal(2, loaded.RetryCount);
        Assert.True(File.Exists(store.FilePath));
        Assert.EndsWith(Path.Combine("state", "demo", "updater-state.json"), store.FilePath);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull()
    {
        using var workspace = new TestWorkspace();
        var store = new StateStore("demo", workspace.Resolve("state"));
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        using var workspace = new TestWorkspace();

        Assert.Null(new StateStore("demo", workspace.Resolve("state")).Load());
    }

    [Fact]
    public void AppIdWithInvalidChars_SanitizedInDirectoryName()
    {
        using var workspace = new TestWorkspace();
        var store = new StateStore("a/b:c", workspace.Resolve("state"));

        Assert.Equal("a_b_c", Path.GetFileName(store.Directory));
    }

    [Fact]
    public void EmptyAppId_UsesDefaultSegment()
    {
        using var workspace = new TestWorkspace();
        var store = new StateStore("", workspace.Resolve("state"));

        Assert.Equal("default", Path.GetFileName(store.Directory));
    }
}

public class GlobalMutexTests
{
    [Fact]
    public async Task SecondAcquire_FailsWhileHeld_AndSucceedsAfterRelease()
    {
        var appId = "test-" + Guid.NewGuid().ToString("N");
        var first = GlobalMutex.TryAcquire(appId, 0, null, out var reason);
        Assert.NotNull(first);
        Assert.Empty(reason);

        try
        {
            // Windows 命名互斥体同线程可重入，必须在其他线程上验证占用
            var blocked = await Task.Run(() => GlobalMutex.TryAcquire(appId, 0, null, out _));
            Assert.Null(blocked);
        }
        finally
        {
            first!.Dispose();
        }

        var again = await Task.Run(() => GlobalMutex.TryAcquire(appId, 0, null, out _));
        Assert.NotNull(again);
        again!.Dispose();
    }
}
