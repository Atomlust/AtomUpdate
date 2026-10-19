using AtomUpdate.Api.Data.Entities;

namespace AtomUpdate.Api.Cache;

/// <summary>
/// 读路径旁路缓存（02 §3.1）：按字符串键缓存解析/聚合结果（键按用途划分命名空间，同键值类型恒定），
/// 未命中经 <c>load</c> 回源并回填（含空结果）；写路径变更后 <see cref="InvalidateAll"/> 抬升全局 epoch
/// 使全部条目立即过期——宁可多回源，绝不返回脏数据。无 Redis 时退回内存 + 短 TTL（Cache:ReadTtlSeconds，默认 30s）。
/// </summary>
public interface IReadCache
{
    /// <summary>取缓存值；未命中时经 <paramref name="load"/> 回源并写入（含空结果）。</summary>
    Task<T> GetAsync<T>(string key, Func<CancellationToken, Task<T>> load, CancellationToken ct);

    /// <summary>全量失效：项目/应用/策略/发布等写路径变更后调用，所有条目下个读请求即回源。</summary>
    void InvalidateAll();
}

/// <summary>项目解析结果：匿名跨租户反查时同 key 多租户命中为歧义（check/report fail-closed，宁拒不泄）。</summary>
public sealed record ProjectLookup(Project? Project, bool Ambiguous);
