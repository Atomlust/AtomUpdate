using System.Threading.Channels;
using AtomUpdate.Api.Data.Entities;

namespace AtomUpdate.Api.Telemetry;

/// <summary>上报队列条目：DownloadLog（安装报告因幂等语义走同步插入，不经队列）。</summary>
public sealed class ReportQueueItem
{
    public required DownloadLog Download { get; init; }
}

/// <summary>下载上报入队（有界 10000；满时丢弃最新并告警，上报端点不阻塞）。</summary>
public sealed class ReportQueue(ILogger<ReportQueue> logger)
{
    private const int Capacity = 10_000;

    private readonly Channel<ReportQueueItem> _channel =
        System.Threading.Channels.Channel.CreateBounded<ReportQueueItem>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public ChannelReader<ReportQueueItem> Reader => _channel.Reader;

    public void Enqueue(ReportQueueItem item)
    {
        if (!_channel.Writer.TryWrite(item))
        {
            logger.LogWarning("上报队列已满（{Capacity}），丢弃 app={AppId} installation={InstallationId} 的记录",
                Capacity, item.Download.AppId, item.Download.InstallationId);
        }
    }
}
