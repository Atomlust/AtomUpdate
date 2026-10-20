using System.Net;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Check;
using AtomUpdate.Configuration;
using AtomUpdate.Logging;
using AtomUpdate.Policy;
using AtomUpdate.Tests.Sdk.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomUpdate.Tests.Sdk;

/// <summary>UpdateChecker 全场景：200/304/错误分支/重试/ETag/策略缓存（04 §3.10）。</summary>
public class UpdateCheckerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "atomupdate-tests", Guid.NewGuid().ToString("N"));

    private AtomUpdateOptions Options() => new()
    {
        ServerUrl = "http://localhost:5062",
        ProjectKey = "test",
        AppId = "com.test.app",
        CurrentVersion = "1.0.0",
        Cache = new CacheOptions { CacheDirectory = _tempRoot }
    };

    private (UpdateChecker Checker, FakeHttpHandler Handler, PolicyCache Cache) Create(AtomUpdateOptions options)
    {
        var handler = new FakeHttpHandler();
        var cache = new PolicyCache(options.AppId, options);
        var checker = new UpdateChecker(new FakeHttpClientFactory(handler), options, cache,
            new ExtensionsUpdateLogger(NullLoggerFactory.Instance));
        return (checker, handler, cache);
    }

    private static CheckPayload? Payload(OperationResult<CheckPayload> result) => result.Data;

    [Fact]
    public async Task NoUpdate_200_ReturnsHasUpdateFalse()
    {
        var options = Options();
        var (checker, handler, _) = Create(options);
        handler.Enqueue(FakeHttpHandler.Json(HttpStatusCode.OK, new
        {
            hasUpdate = false,
            latestVersion = "1.0.0"
        }));

        var result = await checker.CheckAsync(CheckContextOf(options));

        Assert.True(result.Success);
        Assert.False(Payload(result)!.HasUpdate);
        Assert.Null(Payload(result)!.Update);
    }

    [Fact]
    public async Task HasUpdate_WithDelta_ResolvesDeltaFirst()
    {
        var options = Options();
        var (checker, handler, _) = Create(options);
        handler.Enqueue(FakeHttpHandler.Json(HttpStatusCode.OK, new
        {
            hasUpdate = true,
            latestVersion = "2.0.0",
            forceUpdate = false,
            policy = PolicyJson(2),
            package = DeltaJson(),
            fullPackage = FullJson()
        }, "\"v2hash\""));

        var result = await checker.CheckAsync(CheckContextOf(options));

        Assert.True(result.Success);
        var payload = Payload(result)!;
        Assert.True(payload.HasUpdate);
        Assert.NotNull(payload.Update);
        Assert.NotNull(payload.Update.Package); // 增量包优先
        Assert.Equal("2.0.0", payload.Update.LatestVersion);
        Assert.Equal(2, payload.PolicyVersion);
        Assert.Equal("\"v2hash\"", payload.ETag);
    }

    [Fact]
    public async Task NotModified_WithCachedPayload_ReturnsCachedPayload()
    {
        var options = Options();
        var (checker, handler, cache) = Create(options);
        handler.Enqueue(FakeHttpHandler.Json(HttpStatusCode.OK, new
        {
            hasUpdate = true,
            latestVersion = "2.0.0",
            package = FullJson()
        }, "\"etag-1\""));
        await checker.CheckAsync(CheckContextOf(options));

        // 第二次检查：同样请求带 If-None-Match，服务端 304
        var etagOfSecondRequest = "";
        handler.Enqueue(request =>
        {
            etagOfSecondRequest = request.Headers.IfNoneMatch.ToString();
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        var second = await checker.CheckAsync(CheckContextOf(options, etag: "\"etag-1\""));

        Assert.True(second.Success);
        Assert.True(Payload(second)!.HasUpdate);
        Assert.Equal("2.0.0", Payload(second)!.Update!.LatestVersion);
        Assert.Contains("\"etag-1\"", etagOfSecondRequest);
    }

    [Fact]
    public async Task ServerError_ReturnsServerError_NoRetry()
    {
        var options = Options();
        var (checker, handler, _) = Create(options);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await checker.CheckAsync(CheckContextOf(options));

        Assert.False(result.Success);
        Assert.Equal("SERVER_ERROR", result.ErrorCode);
        Assert.Single(handler.Requests); // 业务错误不重试
    }

    [Fact]
    public async Task NetworkError_RetriesExponentially_ThenNetworkError()
    {
        var options = Options();
        options.Network.RetryCount = 2;
        var (checker, handler, _) = Create(options);
        handler.Enqueue(_ => throw new HttpRequestException("boom"));
        handler.Enqueue(_ => throw new HttpRequestException("boom"));
        handler.Enqueue(_ => throw new HttpRequestException("boom"));

        var result = await checker.CheckAsync(CheckContextOf(options));

        Assert.False(result.Success);
        Assert.Equal("NETWORK_ERROR", result.ErrorCode);
        Assert.Equal(3, handler.Requests.Count); // 1 + RetryCount
    }

    [Fact]
    public async Task Success_PersistsPolicyCache()
    {
        var options = Options();
        var (checker, handler, cache) = Create(options);
        handler.Enqueue(FakeHttpHandler.Json(HttpStatusCode.OK, new
        {
            hasUpdate = false,
            latestVersion = "1.0.0",
            policy = PolicyJson(5)
        }, "\"etag-5\""));

        await checker.CheckAsync(CheckContextOf(options));

        var cached = await cache.LoadAsync();
        Assert.NotNull(cached);
        Assert.Equal(5, cached!.PolicyVersion);
        Assert.Equal("\"etag-5\"", cached.ETag);
        Assert.NotNull(cached.Policy);
        Assert.Equal(5, cached.Policy!.Version);
    }

    /// <summary>policyUnchanged 时服务端省略 policy：缓存要保留上一次的服务端策略，不能被 null 覆盖（E2E 回归）。</summary>
    [Fact]
    public async Task PolicyUnchanged_KeepsCachedServerPolicy()
    {
        var options = Options();
        var (checker, handler, cache) = Create(options);
        handler.Enqueue(FakeHttpHandler.Json(HttpStatusCode.OK, new
        {
            hasUpdate = false,
            latestVersion = "1.0.0",
            policy = PolicyJson(5)
        }, "\"etag-5\""));
        await checker.CheckAsync(CheckContextOf(options));

        handler.Enqueue(FakeHttpHandler.Json(HttpStatusCode.OK, new
        {
            hasUpdate = true,
            latestVersion = "2.0.0",
            policyUnchanged = true,
            package = FullJson()
        }));
        var second = await checker.CheckAsync(CheckContextOf(options, policyVersion: 5));

        Assert.True(second.Success);
        Assert.True(Payload(second)!.HasUpdate);
        var cached = await cache.LoadAsync();
        Assert.Equal(5, cached!.PolicyVersion);
        Assert.Equal(5, cached.Policy!.Version);
    }

    [Fact]
    public async Task ETagDisabled_NoIfNoneMatch()
    {
        var options = Options();
        options.Cache.EnableETag = false;
        var (checker, handler, _) = Create(options);
        handler.Enqueue(FakeHttpHandler.Json(HttpStatusCode.OK, new { hasUpdate = false, latestVersion = "1.0.0" }));

        await checker.CheckAsync(CheckContextOf(options, etag: "\"etag-x\""));

        var request = handler.Requests.Single();
        Assert.Empty(request.Headers.IfNoneMatch);
    }

    private static CheckContext CheckContextOf(AtomUpdateOptions options, string? etag = null,
        int policyVersion = 0) => new()
    {
        Trigger = Core.Enums.UpdateTrigger.Polling,
        ProjectKey = options.ProjectKey,
        AppId = options.AppId,
        CurrentVersion = options.CurrentVersion,
        InstallationId = Guid.NewGuid().ToString("D"),
        PolicyVersion = policyVersion,
        ETag = etag
    };

    private static object PolicyJson(int version) => new
    {
        version,
        pollingEnabled = true,
        pollingIntervalMinutes = 360,
        pollingJitterMinutes = 10,
        downloadOnStartup = true,
        downloadOnPolling = false,
        downloadOnManualCheck = false,
        downloadOnForceUpdate = true,
        downloadOnNetworkRecovery = false,
        downloadOnResume = false,
        installMode = "onRestart",
        allowForceUpdate = false,
        allowDowngrade = false,
        allowPostpone = true,
        maxPostponeCount = 3,
        allowMeteredNetwork = false,
        maxDownloadSpeedKBps = 0,
        updatedAt = DateTimeOffset.UtcNow
    };

    private static object FullJson() => new
    {
        packageType = "zip",
        arch = "x64",
        url = "https://cdn.test/app-2.0.0-full.zip",
        sha256 = new string('a', 64),
        size = 1024L
    };

    private static object DeltaJson() => new
    {
        type = "delta",
        packageType = "zip",
        arch = "x64",
        fromVersion = "1.0.0",
        url = "https://cdn.test/app-1.0.0-to-2.0.0.zip",
        sha256 = new string('b', 64),
        size = 256L
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
