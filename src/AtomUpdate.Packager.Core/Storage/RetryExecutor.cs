namespace AtomUpdate.Packager.Core.Storage;

/// <summary>瞬态失败重试（docs/05 §9）：最多 3 次尝试，指数退避 1s/2s/4s；
/// 取消与非瞬态异常立即抛出。</summary>
public static class RetryExecutor
{
    public const int DefaultMaxAttempts = 3;
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(1);

    public static async Task ExecuteAsync(Func<CancellationToken, Task> action,
        Func<Exception, bool>? isTransient = null, int maxAttempts = DefaultMaxAttempts,
        TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct =>
        {
            await action(ct);
            return true;
        }, isTransient, maxAttempts, initialDelay, cancellationToken);
    }

    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action,
        Func<Exception, bool>? isTransient = null, int maxAttempts = DefaultMaxAttempts,
        TimeSpan? initialDelay = null, CancellationToken cancellationToken = default)
    {
        isTransient ??= IsDefaultTransient;
        var delay = initialDelay ?? DefaultInitialDelay;

        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception exception) when (isTransient(exception))
            {
                lastError = exception;
                if (attempt == maxAttempts)
                {
                    break;
                }

                await Task.Delay(delay, cancellationToken);
                delay = Multiply(delay);
            }
        }

        throw new PackagerException(PackagerErrorCodes.UploadFailed,
            $"重试 {maxAttempts} 次后仍失败：{lastError?.Message}", lastError);
    }

    private static TimeSpan Multiply(TimeSpan delay) => TimeSpan.FromTicks(delay.Ticks * 2);

    /// <summary>默认瞬态：网络 IO / HTTP 类异常。</summary>
    private static bool IsDefaultTransient(Exception exception) =>
        exception is IOException or HttpRequestException or TimeoutException;
}
