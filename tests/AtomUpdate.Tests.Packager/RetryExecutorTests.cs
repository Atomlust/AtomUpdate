using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Storage;

namespace AtomUpdate.Tests.Packager;

/// <summary>重试执行器测试（docs/05 §10）：首次成功、非瞬态立即抛、瞬态重试耗尽、取消立即传播。</summary>
public sealed class RetryExecutorTests
{
    private static readonly TimeSpan NoDelay = TimeSpan.Zero;

    [Fact]
    public async Task Success_FirstAttempt_DoesNotRetry()
    {
        var attempts = 0;
        await RetryExecutor.ExecuteAsync(_ =>
        {
            attempts++;
            return Task.CompletedTask;
        }, initialDelay: NoDelay);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task NonTransient_ThrowsImmediately()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => RetryExecutor.ExecuteAsync(
            _ =>
            {
                attempts++;
                throw new InvalidOperationException("配置错误");
            },
            isTransient: _ => false, initialDelay: NoDelay));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Transient_RetriesUpToMaxAttempts_ThenWrapsUploadFailed()
    {
        var attempts = 0;
        var exception = await Assert.ThrowsAsync<PackagerException>(() => RetryExecutor.ExecuteAsync(
            _ =>
            {
                attempts++;
                throw new HttpRequestException("连接重置");
            },
            maxAttempts: 3, initialDelay: NoDelay));

        Assert.Equal(3, attempts);
        Assert.Equal(PackagerErrorCodes.UploadFailed, exception.Code);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task Transient_SucceedsOnSecondAttempt()
    {
        var attempts = 0;
        var result = await RetryExecutor.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(attempts == 2 ? "ok" : throw new IOException("抖动"));
            },
            maxAttempts: 3, initialDelay: NoDelay);

        Assert.Equal(2, attempts);
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Canceled_BeforeStart_ThrowsImmediately()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RetryExecutor.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.CompletedTask;
            },
            initialDelay: NoDelay, cancellationToken: cts.Token));
        Assert.Equal(0, attempts);
    }
}
