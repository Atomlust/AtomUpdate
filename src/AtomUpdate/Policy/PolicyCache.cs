using System.Text.Json;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Core.Serialization;
using AtomUpdate.Internal;

namespace AtomUpdate.Policy;

/// <summary>策略缓存（04 §3.8）：cache/{appId}/policy.json，按应用隔离；EnablePolicyCache=false 时仅内存。</summary>
public sealed class PolicyCache(string appId, AtomUpdate.Configuration.AtomUpdateOptions options)
{
    private static readonly JsonSerializerOptions Json = AtomUpdateJson.Create();

    private readonly string? _filePath = options.Cache.EnablePolicyCache
        ? Path.Combine(PlatformPaths.ResolveCacheRoot(options.Cache.CacheDirectory),
            PlatformPaths.Sanitize(appId), "policy.json")
        : null;

    private CachedPolicy? _memory;

    public async ValueTask<CachedPolicy?> LoadAsync(CancellationToken ct = default)
    {
        if (_memory is not null)
        {
            return _memory;
        }

        if (_filePath is null || !File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var cached = JsonSerializer.Deserialize<CachedPolicy>(json, Json);
            if (cached is not null && options.Cache.CacheExpiration is { } expiration &&
                cached.LastCheckedAt is { } checkedAt &&
                DateTimeOffset.UtcNow - checkedAt > expiration)
            {
                return null;
            }

            _memory = cached;
            return cached;
        }
        catch (Exception)
        {
            // 缓存损坏视为无缓存，下次检查重建
            return null;
        }
    }

    public async ValueTask SaveAsync(CachedPolicy cached, CancellationToken ct = default)
    {
        _memory = cached;
        if (_filePath is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(cached, Json);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temp = _filePath + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct);
        File.Move(temp, _filePath, overwrite: true);
    }

    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        _memory = null;
        if (_filePath is not null && File.Exists(_filePath))
        {
            await Task.Run(() => File.Delete(_filePath), ct);
        }
    }
}
