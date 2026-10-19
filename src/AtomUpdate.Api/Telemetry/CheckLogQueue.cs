using System.Threading.Channels;
using AtomUpdate.Api.Data.Entities;

namespace AtomUpdate.Api.Telemetry;

/// <summary>检查日志入队（有界 10000；满时 TryWrite 返回 false 并丢弃最新，读路径不阻塞）。</summary>
public sealed class CheckLogQueue(ILogger<CheckLogQueue> logger)
{
    private const int Capacity = 10_000;

    // FullMode 保持 Wait：TryWrite 在队满时显式返回 false，丢弃行为由本类控制以便告警
    private readonly Channel<UpdateCheckLog> _channel =
        System.Threading.Channels.Channel.CreateBounded<UpdateCheckLog>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public ChannelReader<UpdateCheckLog> Reader => _channel.Reader;

    public void Enqueue(UpdateCheckLog log)
    {
        if (!_channel.Writer.TryWrite(log))
        {
            logger.LogWarning("检查日志队列已满（{Capacity}），丢弃 app={AppId} installation={InstallationId} 的记录",
                Capacity, log.AppId, log.InstallationId);
        }
    }
}
