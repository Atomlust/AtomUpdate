using System.Net;
using System.Security.Cryptography;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Check;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Enums;
using AtomUpdate.Download;
using AtomUpdate.Logging;
using AtomUpdate.Tests.Sdk.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomUpdate.Tests.Sdk;

/// <summary>下载器：SHA256 校验/断点续传/无进展超时/限速/重试（W3 出口，Q20）。</summary>
public class UpdateDownloaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "atomupdate-tests", Guid.NewGuid().ToString("N"));

    private AtomUpdateOptions Options() => new()
    {
        ServerUrl = "http://localhost:5062",
        ProjectKey = "test",
        AppId = "com.test.app",
        CurrentVersion = "1.0.0",
        Download = new DownloadOptions { TempDirectory = Path.Combine(_tempRoot, "downloads") }
    };

    private UpdateDownloader Create(AtomUpdateOptions options, FakeHttpHandler handler) =>
        new(new FakeHttpClientFactory(handler), options, new ExtensionsUpdateLogger(NullLoggerFactory.Instance));

    private static DownloadContext Context(AtomUpdateOptions options, byte[] payload, string fileName = "app-2.0.0.zip",
        long? size = null) => new()
    {
        Update = new UpdateInfo { LatestVersion = "2.0.0" },
        Package = new PackageAssetRef
        {
            Url = $"https://cdn.test/{fileName}",
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(payload)),
            PackageType = "zip",
            Size = size
        },
        Mode = DownloadMode.Silent,
        DestinationDirectory = options.Download.TempDirectory!,
        MaxSpeedBytesPerSecond = options.Download.MaxDownloadSpeedKBps * 1024L,
        StallTimeoutSeconds = options.Download.StallTimeoutSeconds
    };

    [Fact]
    public async Task Download_Ok_ShaMatches_WritesFile()
    {
        var options = Options();
        var payload = "hello atomupdate"u8.ToArray();
        var downloader = Create(options, HandlerFor(payload));
        var context = Context(options, payload);
        var progresses = new List<DownloadProgress>();

        var result = await downloader.DownloadAsync(context, new Progress<DownloadProgress>(progresses.Add));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(result.PackagePath!));
        Assert.Equal((long)payload.Length, result.Bytes); // 上报用的实际落地字节数
    }

    [Fact]
    public async Task Download_ShaMismatch_FailsVerify_NoRetry()
    {
        var options = Options();
        var payload = "content"u8.ToArray();
        var handler = HandlerFor(payload);
        var downloader = Create(options, handler);
        var context = Context(options, payload);
        var broken = new DownloadContext
        {
            Update = context.Update,
            Package = context.Package with { Sha256 = new string('0', 64) },
            Mode = context.Mode,
            DestinationDirectory = context.DestinationDirectory,
            StallTimeoutSeconds = 60
        };

        var result = await downloader.DownloadAsync(broken, null);

        Assert.False(result.Success);
        Assert.Equal("VERIFY_FAILED", result.ErrorCode);
        Assert.Single(handler.Requests); // 校验失败不重试
    }

    [Fact]
    public async Task Download_ServerError_Retries()
    {
        var options = Options();
        options.Download.RetryCount = 2;
        var payload = "data"u8.ToArray();
        var handler = new FakeHttpHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
        var downloader = Create(options, handler);

        var result = await downloader.DownloadAsync(Context(options, payload), null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Download_Resume_SendsRange_And_Appends()
    {
        var options = Options();
        var firstHalf = "aaaa"u8.ToArray();
        var secondHalf = "bbbb"u8.ToArray();
        var payload = firstHalf.Concat(secondHalf).ToArray();

        var context = Context(options, payload);
        Directory.CreateDirectory(context.DestinationDirectory);
        var partialPath = Path.Combine(context.DestinationDirectory, "2.0.0-app-2.0.0.zip.part");
        await File.WriteAllBytesAsync(partialPath, firstHalf);

        var handler = new FakeHttpHandler();
        string? rangeHeader = null;
        handler.Enqueue(request =>
        {
            rangeHeader = request.Headers.Range?.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(secondHalf)
            };
            response.Content.Headers.ContentRange =
                new System.Net.Http.Headers.ContentRangeHeaderValue(firstHalf.Length, payload.Length - 1);
            return response;
        });
        var downloader = Create(options, handler);

        var result = await downloader.DownloadAsync(context, null);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains($"bytes={firstHalf.Length}-", rangeHeader);
        Assert.Equal(payload, await File.ReadAllBytesAsync(result.PackagePath!));
        Assert.Equal((long)payload.Length, result.Bytes); // 续传后按完整文件大小上报
    }

    [Fact]
    public async Task Download_StallTimeout_FailsWithTimeout()
    {
        var options = Options();
        options.Download.StallTimeoutSeconds = 1;
        var handler = new FakeHttpHandler();
        // 每次重试都返回停滞响应，末次失败原因即无进展超时
        for (var attempt = 0; attempt <= options.Download.RetryCount; attempt++)
        {
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StallStream())
            });
        }
        var downloader = Create(options, handler);

        var result = await downloader.DownloadAsync(Context(options, "never-completes"u8.ToArray()), null);

        Assert.False(result.Success);
        Assert.Contains("无进展超时", result.ErrorMessage);
    }

    [Fact]
    public async Task Download_RequireSignature_MissingSignature_Fails()
    {
        var options = Options();
        options.Verify.RequireSignature = true;
        var payload = "signed?"u8.ToArray();
        var handler = HandlerFor(payload);
        var downloader = Create(options, handler);
        var context = Context(options, payload);

        var result = await downloader.DownloadAsync(context, null);

        Assert.False(result.Success);
        Assert.Equal("VERIFY_FAILED", result.ErrorCode);
    }

    [Fact]
    public async Task Download_SpeedLimit_Respected()
    {
        var options = Options();
        options.Download.StallTimeoutSeconds = 0;
        // 1KB/s 限速，下载 4KB → 至少约 3 秒
        options.Download.MaxDownloadSpeedKBps = 1;
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        var downloader = Create(options, HandlerFor(payload));

        var start = DateTimeOffset.UtcNow;
        var result = await downloader.DownloadAsync(Context(options, payload), null);
        var elapsed = DateTimeOffset.UtcNow - start;

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(elapsed.TotalMilliseconds >= 2500, $"限速未生效：仅耗时 {elapsed.TotalMilliseconds}ms");
    }

    private static FakeHttpHandler HandlerFor(byte[] payload)
    {
        var handler = new FakeHttpHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
        return handler;
    }

    /// <summary>首字节后永不返回且不响应取消的流：模拟真实网络停滞（读端无法取消，只能靠看门狗判定）。</summary>
    private sealed class StallStream : Stream
    {
        private bool _firstDone;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => ReadCore(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) => ReadCore(buffer);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_firstDone)
            {
                _firstDone = true;
                buffer.Span[0] = 1;
                return 1;
            }

            await Task.Delay(Timeout.Infinite, CancellationToken.None);
            return 0;
        }

        private int ReadCore(Span<byte> buffer)
        {
            if (!_firstDone)
            {
                _firstDone = true;
                buffer[0] = 1;
                return 1;
            }

            Task.Delay(Timeout.Infinite, CancellationToken.None).Wait();
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
