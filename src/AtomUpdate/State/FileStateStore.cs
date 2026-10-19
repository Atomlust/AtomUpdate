using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Internal;

namespace AtomUpdate.State;

/// <summary>键值文件持久化（04 §3.3 默认实现），按 appId 隔离目录 state/{appId}/。rootOverride 供测试隔离。</summary>
public sealed class FileStateStore(string appId, string? rootOverride = null) : IStateStore
{
    private readonly string _directory = rootOverride is null
        ? PlatformPaths.AppScoped("state", appId)
        : Path.Combine(rootOverride, PlatformPaths.Sanitize(appId));

    private string PathOf(string key) => Path.Combine(_directory, SanitizeKey(key) + ".json");

    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task SetStringAsync(string key, string value, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(_directory);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, value, ct);
        File.Move(temp, path, overwrite: true);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static string SanitizeKey(string key) => key.Replace('/', '_').Replace('\\', '_');
}
