using System.Net;
using System.Text.Json;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Core.Enums;

namespace AtomUpdate.Tests.Sdk.Infrastructure;

/// <summary>脚本化 HTTP 假处理器：按入队响应依次返回，并记录请求。</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Enqueue(HttpResponseMessage response) =>
        _responses.Enqueue(_ => response);

    public void Enqueue(HttpStatusCode status) =>
        _responses.Enqueue(_ => new HttpResponseMessage(status));

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory) =>
        _responses.Enqueue(factory);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        if (_responses.Count == 0)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object payload, string? etag = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SdkTestJson.Options),
                System.Text.Encoding.UTF8, "application/json")
        };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        return response;
    }
}

/// <summary>测试用 IHttpClientFactory（handler 共享不销毁，BaseAddress 与生产 DI 注册一致）。</summary>
public sealed class FakeHttpClientFactory(FakeHttpHandler handler, string baseAddress = "http://localhost:5062/") : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
}

public static class SdkTestJson
{
    public static JsonSerializerOptions Options { get; } =
        AtomUpdate.Core.Serialization.AtomUpdateJson.Create();
}

public sealed class FakeChecker(OperationResult<CheckPayload> result) : IUpdateChecker
{
    public int Calls { get; private set; }

    public Task<OperationResult<CheckPayload>> CheckAsync(CheckContext context, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(result);
    }
}

public sealed class FakeDownloader : IUpdateDownloader
{
    public List<(DownloadContext Context, DownloadMode Mode)> Calls { get; } = [];

    public Func<DownloadContext, DownloadResult> Respond { get; set; } = _ =>
        DownloadResult.Fail("DOWNLOAD_FAILED", "未配置");

    /// <summary>下载前依次投递的进度序列。</summary>
    public List<DownloadProgress> ProgressReports { get; } = [];

    /// <summary>每投递一次进度后的回调（入参为已投递次数），用于断言投递是同步有序的。</summary>
    public Action<int>? AfterEachReport { get; set; }

    public async Task<DownloadResult> DownloadAsync(DownloadContext context, IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        Calls.Add((context, context.Mode));
        for (var i = 0; i < ProgressReports.Count; i++)
        {
            progress?.Report(ProgressReports[i]);
            AfterEachReport?.Invoke(i + 1);
            await Task.Yield();
        }

        return Respond(context);
    }
}

public sealed class FakeInstaller : IUpdateInstaller
{
    public List<InstallContext> Calls { get; } = [];

    public InstallResult Respond { get; set; } = InstallResult.ScheduledOk();

    public Task<InstallResult> InstallAsync(InstallContext context, CancellationToken ct = default)
    {
        Calls.Add(context);
        return Task.FromResult(Respond);
    }
}

public sealed class FakeNotifier : IUpdateNotifier
{
    public List<NotificationContext> Notifications { get; } = [];

    public Task NotifyAsync(NotificationContext context, CancellationToken ct = default)
    {
        Notifications.Add(context);
        return Task.CompletedTask;
    }
}

/// <summary>可配置平台信息（更新器释放/启动配置测试）。</summary>
public sealed class FakePlatformInfo(UpdatePlatform platform = UpdatePlatform.Windows,
    UpdateArch arch = UpdateArch.X64, string? runtimeIdentifier = null) : IPlatformInfo
{
    public UpdatePlatform Platform { get; } = platform;

    public UpdateArch Arch { get; } = arch;

    public string OsVersion => "test-os";

    public string RuntimeIdentifier { get; } = runtimeIdentifier ??
        $"{platform.ToString().ToLowerInvariant()}-{arch.ToString().ToLowerInvariant()}";
}

public sealed class FakeReporter : IUpdateReporter
{
    public List<object> Reports { get; } = [];

    public bool Succeed { get; set; } = true;

    public Task<bool> ReportCheckAsync(CheckReport report, CancellationToken ct = default)
    {
        Reports.Add(report);
        return Task.FromResult(Succeed);
    }

    public Task<bool> ReportDownloadAsync(DownloadReport report, CancellationToken ct = default)
    {
        Reports.Add(report);
        return Task.FromResult(Succeed);
    }

    public Task<bool> ReportInstallAsync(InstallReport? report, CancellationToken ct = default)
    {
        Reports.Add(report!);
        return Task.FromResult(Succeed);
    }

    public Task<bool> ReportStartupAsync(StartupReport report, CancellationToken ct = default)
    {
        Reports.Add(report);
        return Task.FromResult(Succeed);
    }

    public Task<bool> ReportBatchAsync(IReadOnlyList<object> events, CancellationToken ct = default)
    {
        Reports.AddRange(events);
        return Task.FromResult(Succeed);
    }
}
