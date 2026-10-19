using System.Collections.Concurrent;

namespace AtomUpdate.Api.Cache;

/// <summary>内存旁路缓存实现：epoch 全量失效 + 短 TTL + 条目数上限（先清过期，仍超限整体清空，防内存膨胀）。</summary>
public sealed class MemoryReadCache(IConfiguration configuration) : IReadCache
{
    private sealed class Entry
    {
        public required long Epoch { get; init; }
        public required DateTimeOffset LoadedAt { get; init; }
        public required object? Value { get; init; }
    }

    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(
        Math.Max(configuration.GetValue("Cache:ReadTtlSeconds", 30), 1));

    // 上限覆盖解析键（租户 × projectKey/appId/策略）与统计键的合理量级；超限走 fail-safe 清理
    private const int MaxEntries = 4096;

    private long _epoch;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public async Task<T> GetAsync<T>(string key, Func<CancellationToken, Task<T>> load, CancellationToken ct)
    {
        var epoch = Interlocked.Read(ref _epoch);
        if (_entries.TryGetValue(key, out var entry) &&
            entry.Epoch == epoch &&
            DateTimeOffset.UtcNow - entry.LoadedAt < _ttl)
        {
            return (T)entry.Value!;
        }

        var value = await load(ct).ConfigureAwait(false);

        // 回填用加载前捕获的 epoch：加载期间发生变更（epoch 已抬升）则条目落库即过期，下次读自动回源
        if (_entries.Count >= MaxEntries)
        {
            Prune();
        }

        _entries[key] = new Entry { Epoch = epoch, LoadedAt = DateTimeOffset.UtcNow, Value = value };
        return value;
    }

    public void InvalidateAll() => Interlocked.Increment(ref _epoch);

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
