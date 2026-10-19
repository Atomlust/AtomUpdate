using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AtomUpdate.Abstractions.Interfaces;
using AtomUpdate.Abstractions.Models;
using AtomUpdate.Check;
using AtomUpdate.Configuration;
using AtomUpdate.Core.Constants;

namespace AtomUpdate.Download;

/// <summary>下载器（04 §3.10/§3.16）：流式 + IProgress、断点续传（Range/206）、无进展超时（Q20）、限速、SHA256 强制校验、失败重试。</summary>
public sealed class UpdateDownloader(
    IHttpClientFactory httpClientFactory,
    AtomUpdateOptions options,
    IUpdateLogger logger) : IUpdateDownloader
{
    public async Task<DownloadResult> DownloadAsync(DownloadContext context, IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        var targetPath = ResolveTargetPath(context);
        DownloadResult? last = null;

        for (var attempt = 0; attempt <= options.Download.RetryCount; attempt++)
        {
            if (attempt > 0)
            {
                logger.Warning($"下载失败，准备第 {attempt}/{options.Download.RetryCount} 次重试（断点续传）");
            }

            var result = await DownloadOnceAsync(context, targetPath, progress, ct);
            if (result.Success)
            {
                return result;
            }

            if (result.ErrorCode == ErrorCodes.VerifyFailed)
            {
                // 校验失败重试无意义：直接放弃
                return result;
            }

            if (ct.IsCancellationRequested)
            {
                return DownloadResult.Fail(ErrorCodes.DownloadFailed, "下载已取消");
            }

            last = result;
        }

        // 重试耗尽：保留末次失败原因（如无进展超时），便于宿主按错误码分支
        return DownloadResult.Fail(last!.ErrorCode!,
            $"下载失败（已重试 {options.Download.RetryCount} 次）：{last.ErrorMessage}");
    }

    private async Task<DownloadResult> DownloadOnceAsync(DownloadContext context, string targetPath,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(AtomUpdateHttp.DownloadClientName);
        var package = context.Package;
        Directory.CreateDirectory(context.DestinationDirectory);
        var partialPath = targetPath + ".part";
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, package.Url);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (context.StallTimeoutSeconds > 0)
        {
            stallCts.CancelAfter(TimeSpan.FromSeconds(context.StallTimeoutSeconds));
        }

        // 无进展看门狗：响应流的读未必响应取消（代理停顿/自定义内容），故用超时任务主动判定（Q20）
        var watchdog = context.StallTimeoutSeconds > 0
            ? Task.Delay(Timeout.InfiniteTimeSpan, stallCts.Token)
            : null;

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stallCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return DownloadResult.Fail(ErrorCodes.DownloadFailed,
                $"下载无进展超时（{context.StallTimeoutSeconds}s 无字节进展）");
        }
        catch (Exception exception)
        {
            return DownloadResult.Fail(exception, ErrorCodes.NetworkError, "下载连接失败");
        }

        long totalReceived = 0;
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return DownloadResult.Fail(ErrorCodes.DownloadFailed, $"下载失败：HTTP {(int)response.StatusCode}");
            }

            var resumed = response.StatusCode == HttpStatusCode.PartialContent;
            if (existingLength > 0 && !resumed)
            {
                existingLength = 0; // 服务器不支持续传，重头下载
            }

            long totalBytes = response.Content.Headers.ContentLength is { } length
                ? length + existingLength
                : package.Size ?? 0;

            await using var source = await response.Content.ReadAsStreamAsync(stallCts.Token);
            var writeMode = existingLength > 0 ? FileMode.Append : FileMode.Create;
            await using var target = new FileStream(partialPath, writeMode, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long received = existingLength;
            long bytesSinceReport = 0;
            var lastReport = Stopwatch.GetTimestamp();
            var speedLimit = context.MaxSpeedBytesPerSecond;
            var startTimestamp = Stopwatch.GetTimestamp();

            try
            {
                while (true)
                {
                    var chunkTimestamp = Stopwatch.GetTimestamp();
                    var readTask = source.ReadAsync(buffer, stallCts.Token).AsTask();
                    if (watchdog is not null && await Task.WhenAny(readTask, watchdog) != readTask)
                    {
                        Observe(readTask);
                        return ct.IsCancellationRequested
                            ? DownloadResult.Fail(ErrorCodes.DownloadFailed, "下载已取消")
                            : DownloadResult.Fail(ErrorCodes.DownloadFailed,
                                $"下载无进展超时（{context.StallTimeoutSeconds}s 无字节进展）");
                    }

                    var read = await readTask;
                    if (read == 0)
                    {
                        break;
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), stallCts.Token);
                    received += read;
                    bytesSinceReport += read;

                    // 重置无进展计时
                    if (context.StallTimeoutSeconds > 0)
                    {
                        stallCts.CancelAfter(TimeSpan.FromSeconds(context.StallTimeoutSeconds));
                    }

                    if (progress is not null && (bytesSinceReport >= 65536 ||
                        Stopwatch.GetElapsedTime(lastReport) >= TimeSpan.FromMilliseconds(250)))
                    {
                        var elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp, chunkTimestamp).TotalSeconds;
                        Report(progress, received, totalBytes, elapsedSeconds);
                        bytesSinceReport = 0;
                        lastReport = Stopwatch.GetTimestamp();
                    }

                    // 限速：块传输耗时不足期望值时补足延迟
                    if (speedLimit > 0)
                    {
                        var expectedMilliseconds = read * 1000.0 / speedLimit;
                        var actualMilliseconds = Stopwatch.GetElapsedTime(chunkTimestamp).TotalMilliseconds;
                        if (expectedMilliseconds > actualMilliseconds)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(expectedMilliseconds - actualMilliseconds),
                                stallCts.Token);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return DownloadResult.Fail(ErrorCodes.DownloadFailed,
                    $"下载无进展超时（{context.StallTimeoutSeconds}s 无字节进展）");
            }
            catch (Exception exception)
            {
                return DownloadResult.Fail(exception, ErrorCodes.NetworkError, "下载中断");
            }

            await target.FlushAsync(stallCts.Token);
            totalReceived = received;
        }

        // SHA256 强制校验（不可关闭）；先关闭校验流，否则 Windows 下后续 Move/Delete 会因文件占用失败
        string actualHash;
        {
            using var verifyStream = new FileStream(partialPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            actualHash = Convert.ToHexStringLower(SHA256.HashData(verifyStream));
        }

        if (!string.Equals(actualHash, package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partialPath);
            return DownloadResult.Fail(ErrorCodes.VerifyFailed,
                $"SHA256 校验失败：期望 {package.Sha256}，实际 {actualHash}");
        }

        // 签名验证：默认不验（Verify.RequireSignature 启用后，无签名即失败；公钥内嵌分发归 M4/M5 落地）
        if (options.Verify.RequireSignature)
        {
            if (string.IsNullOrEmpty(package.Signature))
            {
                File.Delete(partialPath);
                return DownloadResult.Fail(ErrorCodes.VerifyFailed, "签名验证已启用但更新包未签名");
            }

            logger.Warning("签名材料校验暂未接入公钥分发（M4/M5），本次跳过密码学验证");
        }

        File.Move(partialPath, targetPath, overwrite: true);
        logger.Information($"下载完成：{targetPath}");
        return DownloadResult.Ok(targetPath, totalReceived);
    }

    private static void Observe(Task task) => task.ContinueWith(static t => _ = t.Exception,
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    private static void Report(IProgress<DownloadProgress> progress, long received, long totalBytes,
        double elapsedSeconds)
    {
        double? percent = totalBytes > 0 ? Math.Min(received * 100.0 / totalBytes, 100) : null;
        var speed = elapsedSeconds > 0 ? received / elapsedSeconds : 0;
        TimeSpan? remaining = totalBytes > 0 && speed > 0
            ? TimeSpan.FromSeconds((totalBytes - received) / speed)
            : null;

        progress.Report(new DownloadProgress
        {
            BytesReceived = received,
            TotalBytes = totalBytes > 0 ? totalBytes : null,
            Percent = percent,
            SpeedBytesPerSecond = speed,
            RemainingTime = remaining
        });
    }

    private static string ResolveTargetPath(DownloadContext context)
    {
        var package = context.Package;
        var fileName = Path.GetFileName(new Uri(package.Url).LocalPath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = $"update-{context.Update.LatestVersion}.{package.PackageType}";
        }

        return Path.Combine(context.DestinationDirectory,
            $"{context.Update.LatestVersion}-{fileName.Replace("..", "_")}");
    }
}
