namespace AtomUpdate.Internal;

/// <summary>同步转发的 IProgress 适配（04 §3.16：事件在后台线程触发）。Progress&lt;T&gt; 会把回调投递到捕获的上下文，
/// 既脱离下载线程，又让多次 Report 并发执行——进度事件因此乱序（UI 会看到百分比倒退）。</summary>
internal sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
