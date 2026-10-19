using System.Text.Json;
using AtomUpdate.Updater.Json;

namespace AtomUpdate.Updater.State;

/// <summary>状态文件读写（04 §4.4）：原子写（临时文件 + 覆盖移动）。</summary>
public sealed class StateStore(string appId, string? stateRootOverride = null)
{
    public string Directory { get; } = stateRootOverride is null
        ? Path.Combine(UpdaterPaths.StateRoot, UpdaterPaths.Sanitize(appId))
        : Path.Combine(stateRootOverride, UpdaterPaths.Sanitize(appId));

    public string FilePath => Path.Combine(Directory, "updater-state.json");

    public UpdaterState? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), UpdaterJsonContext.Default.UpdaterState);
        }
        catch (Exception)
        {
            // 状态文件损坏不阻断安装；按无历史状态处理
            return null;
        }
    }

    public void Save(UpdaterState state)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, UpdaterJsonContext.Default.UpdaterState));
        File.Move(temp, FilePath, overwrite: true);
    }
}
