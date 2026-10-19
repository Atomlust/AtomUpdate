using System.Net;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Check;
using AtomUpdate.Configuration;
using AtomUpdate.Logging;
using AtomUpdate.Report;
using AtomUpdate.Tests.Sdk.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomUpdate.Tests.Sdk;

/// <summary>上报离线兜底（04 §3.14/Q15，W4 出口：断网场景离线补报验证通过）。pending-reports 用 stateRootOverride 落到临时目录，不污染真实用户目录。</summary>
public class UpdateReporterTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "atomupdate-tests", Guid.NewGuid().ToString("N"));

    private AtomUpdateOptions Options() => new()
    {
        ServerUrl = "http://localhost:5062",
        ProjectKey = "test",
        AppId = "com.test.reporter",
        CurrentVersion = "1.0.0"
    };

    private UpdateReporter Create(AtomUpdateOptions options, FakeHttpHandler handler) =>
        new(new FakeHttpClientFactory(handler), options,
            new ExtensionsUpdateLogger(NullLoggerFactory.Instance), _tempRoot);

    private string PendingDirectory() => Path.Combine(_tempRoot, "com.test.reporter", "pending-reports");

    private static CheckReport Report() => new()
    {
        ProjectKey = "test",
        AppId = "com.test.app",
        InstallationId = Guid.NewGuid().ToString("D"),
        CurrentVersion = "1.0.0",
        HasUpdate = true,
        Trigger = Core.Enums.UpdateTrigger.Polling
    };

    [Fact]
    public async Task NetworkFailure_PersistsPendingReport()
    {
        var options = Options();
        var handler = new FakeHttpHandler();
        handler.Enqueue(_ => throw new HttpRequestException("offline"));
        var reporter = Create(options, handler);

        var sent = await reporter.ReportCheckAsync(Report());

        Assert.False(sent);
        Assert.Single(Directory.GetFiles(PendingDirectory()));
    }

    /// <summary>非 2xx（如批量端点未上线的 404）也是失败，同样落盘等补报。</summary>
    [Fact]
    public async Task RejectedByServer_PersistsPendingReport()
    {
        var options = Options();
        var handler = new FakeHttpHandler();
        handler.Enqueue(HttpStatusCode.NotFound);
        var reporter = Create(options, handler);

        var sent = await reporter.ReportCheckAsync(Report());

        Assert.False(sent);
        Assert.Single(Directory.GetFiles(PendingDirectory()));
    }

    [Fact]
    public async Task Startup_FlushesPendingQueue_OnSuccess()
    {
        var options = Options();
        var failing = new FakeHttpHandler();
        failing.Enqueue(_ => throw new HttpRequestException("offline"));
        await Create(options, failing).ReportCheckAsync(Report());
        Assert.Single(Directory.GetFiles(PendingDirectory()));

        var succeeding = new FakeHttpHandler();
        succeeding.Enqueue(HttpStatusCode.OK);
        succeeding.Enqueue(HttpStatusCode.OK);
        var reporter = Create(options, succeeding);

        var startup = await reporter.ReportStartupAsync(new StartupReport
        {
            ProjectKey = "test",
            AppId = "com.test.reporter",
            InstallationId = Guid.NewGuid().ToString("D"),
            Version = "1.0.0"
        });

        Assert.True(startup);
        Assert.Empty(Directory.GetFiles(PendingDirectory())); // 队列清空
        Assert.Equal(2, succeeding.Requests.Count); // 启动回执 + 补报各一次
    }

    [Fact]
    public async Task DisabledReports_NoHttp_NoQueue()
    {
        var options = Options();
        options.Report.Enabled = false;
        var handler = new FakeHttpHandler();
        var reporter = Create(options, handler);

        var sent = await reporter.ReportCheckAsync(Report());

        Assert.True(sent);
        Assert.Empty(handler.Requests);
        Assert.False(Directory.Exists(PendingDirectory()));
    }

    [Fact]
    public async Task OfflineFallbackDisabled_FailsWithoutQueue()
    {
        var options = Options();
        options.Report.FallbackToLocal = false;
        var handler = new FakeHttpHandler();
        handler.Enqueue(_ => throw new HttpRequestException("offline"));
        var reporter = Create(options, handler);

        var sent = await reporter.ReportCheckAsync(Report());

        Assert.False(sent);
        Assert.False(Directory.Exists(PendingDirectory()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
