using AtomUpdate.Abstractions.Enums;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Enums;
using Microsoft.Extensions.Hosting;

namespace AtomUpdate.Polling;

/// <summary>定时轮询（04 §3.16）：BackgroundService 可变间隔循环（Task.Delay）+ Jitter + 失败指数退避（Initial ×2^n → Max）。</summary>
public sealed class PollingService(IAtomUpdater updater, AtomUpdateOptions options, IUpdateLogger logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Polling.Enabled)
        {
            logger.Information("轮询已禁用（Polling.Enabled=false）");
            return;
        }

        if (options.Polling.CheckOnStartup)
        {
            try
            {
                await DelayWithStop(stoppingToken, options.Polling.InitialDelay);
                await updater.CheckAndDownloadAsync(UpdateTrigger.Startup, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.Warning("启动检查失败", exception);
            }
        }

        var backoff = options.Polling.BackoffInitial;
        while (!stoppingToken.IsCancellationRequested)
        {
            // 退避期间使用退避间隔，否则使用正常间隔 ± Jitter
            var interval = backoff > options.Polling.BackoffInitial
                ? backoff
                : NextJitteredInterval();
            try
            {
                await DelayWithStop(stoppingToken, interval);
                var result = await updater.CheckAndDownloadAsync(UpdateTrigger.Polling, stoppingToken);

                if (result.Check.Success)
                {
                    backoff = options.Polling.BackoffInitial; // 成功重置退避
                }
                else
                {
                    backoff = NextBackoff(backoff);
                    logger.Warning($"轮询检查失败，退避至 {backoff.TotalMinutes:F1} 分钟后重试");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                backoff = NextBackoff(backoff);
                logger.Warning("轮询检查异常", exception);
            }
        }
    }

    /// <summary>正常轮询间隔 ± Jitter。</summary>
    private TimeSpan NextJitteredInterval()
    {
        var baseSeconds = options.Polling.Interval.TotalSeconds;
        if (options.Polling.JitterMinutes > 0)
        {
            var jitterSeconds = Random.Shared.NextDouble() * 2 * options.Polling.JitterMinutes * 60
                - options.Polling.JitterMinutes * 60;
            baseSeconds += jitterSeconds;
        }

        return TimeSpan.FromSeconds(Math.Max(baseSeconds, 1));
    }

    private TimeSpan NextBackoff(TimeSpan current)
    {
        var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * options.Polling.BackoffFactor);
        return next > options.Polling.BackoffMax ? options.Polling.BackoffMax : next;
    }

    private static async Task DelayWithStop(CancellationToken ct, TimeSpan delay)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, ct);
        }
    }
}
