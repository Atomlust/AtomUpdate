using System.Collections.Concurrent;

namespace AtomUpdate.Api.Cache;

public sealed class MemoryReleaseCache(IConfiguration configuration) : IReleaseCache
{
    private sealed class Entry
    {
        public required long Epoch { get; init; }
        public required DateTimeOffset LoadedAt { get; init; }
        public CachedRelease? Value { get; init; }
    }

    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(
        Math.Max(configuration.GetValue("Cache:ReleaseTtlSeconds", 30), 1));

    // 条目 = (应用, 通道) 组合，量级可控；超限先清过期、仍超限整体清空（fail-safe，防内存膨胀）
    private const int MaxEntries = 4096;

    private readonly ConcurrentDictionary<long, long> _epochs = new();
    private readonly ConcurrentDictionary<ReleaseCacheKey, Entry> _entries = new();

    public async Task<CachedRelease?> GetLatestAsync(ReleaseCacheKey key,
        Func<CancellationToken, Task<CachedRelease?>> load, CancellationToken ct)
    {
        var epoch = _epochs.GetOrAdd(key.AppId, 0);
        if (_entries.TryGetValue(key, out var entry) &&
            entry.Epoch == epoch &&
            DateTimeOffset.UtcNow - entry.LoadedAt < _ttl)
        {
            return entry.Value;
        }

        var value = await load(ct).ConfigureAwait(false);

        // 回填用加载前捕获的 epoch：加载期间发生发布（epoch 已 bump）则条目落库即过期，下次读自动回源
        if (_entries.Count >= MaxEntries)
        {
            Prune();
        }

        _entries[key] = new Entry { Epoch = epoch, LoadedAt = DateTimeOffset.UtcNow, Value = value };
        return value;
    }

    public void Invalidate(long appId) => _epochs.AddOrUpdate(appId, 1, (_, current) => current + 1);

    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, entry) in _entries)
        {
            if (now - entry.LoadedAt >= _ttl)
            {
                _entries.TryRemove(key, out _);
            }
        }

        if (_entries.Count >= MaxEntries)
        {
            _entries.Clear();
        }
    }
}
