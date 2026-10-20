using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Serialization;
using AtomUpdate.Policy;

namespace AtomUpdate.Check;

/// <summary>检查更新（04 §3.10 HTTP 契约）：POST /api/check + If-None-Match/304 + 网络错误指数退避重试。</summary>
public sealed class UpdateChecker(
    IHttpClientFactory httpClientFactory,
    AtomUpdateOptions options,
    PolicyCache cache,
    IUpdateLogger logger) : IUpdateChecker
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = AtomUpdateJson.Create();

    public async Task<OperationResult<CheckPayload>> CheckAsync(CheckContext context, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(AtomUpdateHttp.ClientName);
        var delay = TimeSpan.FromMilliseconds(500);
        Exception? lastError = null;
        for (var attempt = 0; attempt <= options.Network.RetryCount; attempt++)
        {
            if (attempt > 0)
            {
                logger.Warning($"检查失败，{delay.TotalMilliseconds:F0}ms 后重试（第 {attempt}/{options.Network.RetryCount} 次）", lastError);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));
            }

            OperationResult<CheckPayload> result;
            try
            {
                // HttpRequestMessage 不可重复发送，每轮新建
                using var request = BuildRequest(context);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                result = await HandleResponseAsync(response, context, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                continue;
            }

            if (result.Success)
            {
                return result;
            }

            // 业务错误（服务端 4xx/5xx、响应不合法）不重试
            return result;
        }

        return OperationResult<CheckPayload>.Fail(lastError!, ErrorCodes.NetworkError, "检查更新失败（网络错误，已重试）");
    }

    private HttpRequestMessage BuildRequest(CheckContext context)
    {
        var body = new CheckRequest
        {
            ProjectKey = context.ProjectKey,
            AppId = context.AppId,
            CurrentVersion = context.CurrentVersion,
            Platform = context.Platform,
            Arch = context.Arch,
            Channel = context.Channel,
            InstallationId = context.InstallationId,
            PolicyVersion = context.PolicyVersion
        };

        var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.Check)
        {
            Content = JsonContent.Create(body, options: Json)
        };

        if (options.Cache.EnableETag && !string.IsNullOrEmpty(context.ETag))
        {
            request.Headers.IfNoneMatch.ParseAdd(context.ETag);
        }

        return request;
    }

    private async Task<OperationResult<CheckPayload>> HandleResponseAsync(HttpResponseMessage response,
        CheckContext context, CancellationToken ct)
    {
        var etag = response.Headers.ETag?.Tag;

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            var cached = await cache.LoadAsync(ct);
            if (cached?.LastPayload is not null)
            {
                return OperationResult<CheckPayload>.Ok(cached.LastPayload);
            }

            // 无缓存可重建：视为无更新（ETag 已保证服务端状态未变）
            return OperationResult<CheckPayload>.Ok(new CheckPayload
            {
                HasUpdate = false,
                ETag = etag ?? context.ETag
            });
        }

        if (!response.IsSuccessStatusCode)
        {
            return OperationResult<CheckPayload>.Fail(ErrorCodes.ServerError,
                $"检查更新失败：HTTP {(int)response.StatusCode}");
        }

        CheckResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<CheckResponse>(Json, ct);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or System.Net.Http.HttpRequestException)
        {
            return OperationResult<CheckPayload>.Fail(exception, ErrorCodes.InvalidResponse, "响应内容不合法");
        }

        if (body is null)
        {
            return OperationResult<CheckPayload>.Fail(ErrorCodes.InvalidResponse, "响应内容为空");
        }

        var payload = new CheckPayload
        {
            HasUpdate = body.HasUpdate,
            Update = body.HasUpdate ? ToUpdateInfo(body) : null,
            ServerPolicy = body.Policy,
            PolicyVersion = body.Policy?.Version,
            ETag = etag
        };

        // 服务端仅在策略变化时下发 policy（policyUnchanged 时省略）：缓存要保留上一次的服务端策略，
        // 否则会被 null 覆盖、下次检查退化为内置默认（E2E 曾表现为 Manual 触发不再自动下载）
        var previous = await cache.LoadAsync(ct);
        await cache.SaveAsync(new CachedPolicy
        {
            Policy = body.Policy ?? previous?.Policy,
            PolicyVersion = body.Policy?.Version ?? previous?.PolicyVersion ?? 0,
            ETag = etag,
            LatestVersion = body.LatestVersion,
            LastCheckedAt = DateTimeOffset.UtcNow,
            LastPayload = payload
        }, ct);

        return OperationResult<CheckPayload>.Ok(payload);
    }

    private static UpdateInfo ToUpdateInfo(CheckResponse body) => new()
    {
        LatestVersion = body.LatestVersion,
        ForceUpdate = body.ForceUpdate == true,
        MinSupportedVersion = body.MinSupportedVersion,
        Package = body.Package,
        FullPackage = body.FullPackage,
        ReleaseNotes = body.ReleaseNotes
    };
}

/// <summary>命名 HttpClient 常量。</summary>
public static class AtomUpdateHttp
{
    public const string ClientName = "AtomUpdate";

    /// <summary>下载专用客户端：超时无限（由无进展超时控制）。</summary>
    public const string DownloadClientName = "AtomUpdate.Download";

    /// <summary>上报专用客户端：15 秒超时。</summary>
    public const string ReportClientName = "AtomUpdate.Report";
}
