using System.Net;
using System.Text;
using System.Text.Json;
using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Core.Serialization;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Storage;

namespace AtomUpdate.Packager.Core.Api;

/// <summary>手写 HttpClient 发布客户端（docs/05 §11，偏差：不引 Refit）：AtomUpdateJson 序列化、
/// 网络/5xx 同享 3 次指数退避重试（创建端点按 (appId,version) 幂等，上传为覆盖写，重试均安全）；
/// 409 RELEASE_EXISTS → 专用错误码，其余非 2xx → PACKAGER_API_FAILED。
/// 鉴权：bearerToken（登录会话 JWT，Manage 策略）。</summary>
public sealed class HttpAtomUpdateApiClient : IAtomUpdateApiClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = AtomUpdateJson.Create();

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public HttpAtomUpdateApiClient(string apiUrl, string? bearerToken = null)
        : this(CreateClient(apiUrl, bearerToken), ownsHttp: true)
    {
    }

    internal HttpAtomUpdateApiClient(HttpClient httpClient, bool ownsHttp = false)
    {
        _http = httpClient;
        _ownsHttp = ownsHttp;
    }

    public async Task<ReleaseDto> CreateReleaseAsync(long projectId, long appId, ReleaseCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var (status, body) = await SendAsync(
            HttpMethod.Post, ReleasesRoute(projectId, appId), request, cancellationToken);
        EnsureSuccess(status, body, "推送元数据失败", HttpStatusCode.Created, HttpStatusCode.OK);
        return Deserialize<ReleaseDto>(body)!;
    }

    public async Task<ReleaseDto> PublishReleaseAsync(long projectId, long appId, long releaseId,
        ReleasePublishRequest request, CancellationToken cancellationToken = default)
    {
        var (status, body) = await SendAsync(HttpMethod.Post,
            $"{ReleasesRoute(projectId, appId)}/{releaseId}/publish", request, cancellationToken);
        EnsureSuccess(status, body, "发布版本失败", HttpStatusCode.OK);
        return Deserialize<ReleaseDto>(body)!;
    }

    public async Task<PagedResult<ReleaseDto>> ListReleasesAsync(long projectId, long appId, int page = 1,
        int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var url = $"{ReleasesRoute(projectId, appId)}?page={page}&pageSize={pageSize}";
        var (status, body) = await SendAsync(HttpMethod.Get, url, payload: null, cancellationToken);
        EnsureSuccess(status, body, "拉取版本列表失败", HttpStatusCode.OK);
        return Deserialize<PagedResult<ReleaseDto>>(body)!;
    }

    public async Task<UploadResult> UploadAssetAsync(long projectId, long appId, string version,
        string filePath, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new PackagerException(PackagerErrorCodes.UploadFailed, $"上传文件不存在：{filePath}");
        }

        var route = $"/api/manage/projects/{projectId}/apps/{appId}/upload?version={Uri.EscapeDataString(version)}";
        var fileName = Path.GetFileName(filePath);
        var total = new FileInfo(filePath).Length;

        var (status, body) = await RetryExecutor.ExecuteAsync(async ct =>
        {
            // 每次尝试重新打开文件流（重试时上一次流可能已被释放）；上传为覆盖写，幂等
            using var request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = new MultipartFormDataContent
                {
                    { new StreamContent(new ProgressStream(File.OpenRead(filePath), fileName, total, progress)), "file", fileName }
                }
            };
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseContentRead, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return (response.StatusCode, responseBody);
            }

            if (response.StatusCode >= HttpStatusCode.InternalServerError)
            {
                // 5xx 视为瞬态，交重试器
                throw new HttpRequestException($"服务端暂不可用（{(int)response.StatusCode}）：{Truncate(responseBody)}");
            }

            return (response.StatusCode, responseBody);
        }, isTransient: exception => exception is HttpRequestException or IOException or TimeoutException,
            cancellationToken: cancellationToken);

        EnsureSuccess(status, body, "上传包文件失败", HttpStatusCode.OK);
        var uploaded = Deserialize<AssetUploadResult>(body)
            ?? throw new PackagerException(PackagerErrorCodes.UploadFailed, "上传响应为空");
        return new UploadResult
        {
            Url = uploaded.Url,
            Sha256 = uploaded.Sha256,
            Size = uploaded.Size,
            Filename = uploaded.Filename
        };
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMethod method, string url,
        object? payload, CancellationToken cancellationToken)
    {
        return await RetryExecutor.ExecuteAsync(async ct =>
        {
            using var request = new HttpRequestMessage(method, url);
            if (payload is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseContentRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
            {
                return (response.StatusCode, body);
            }

            if (response.StatusCode >= HttpStatusCode.InternalServerError)
            {
                // 5xx 视为瞬态，交重试器
                throw new HttpRequestException($"服务端暂不可用（{(int)response.StatusCode}）：{Truncate(body)}");
            }

            return (response.StatusCode, body);
        }, isTransient: exception => exception is HttpRequestException or IOException or TimeoutException,
            cancellationToken: cancellationToken);
    }

    private static void EnsureSuccess(HttpStatusCode status, string body, string errorTitle,
        params HttpStatusCode[] allowed)
    {
        if (allowed.Contains(status))
        {
            return;
        }

        ErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            // 非 JSON 错误页，走通用失败
        }

        if (status == HttpStatusCode.Conflict && error?.Code == ErrorCodes.ReleaseExists)
        {
            throw new PackagerException(PackagerErrorCodes.ReleaseExists, error.Message);
        }

        var detail = error is null
            ? $"HTTP {(int)status}：{Truncate(body)}"
            : $"{error.Code}：{error.Message}";
        throw new PackagerException(PackagerErrorCodes.ApiFailed, $"{errorTitle}：{detail}");
    }

    private static string ReleasesRoute(long projectId, long appId) =>
        $"/api/projects/{projectId}/apps/{appId}/releases";

    private static T? Deserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed,
                $"服务端响应解析失败：{Truncate(body)}", exception);
        }
    }

    private static string Truncate(string text) =>
        text.Length <= 500 ? text : text[..500];

    private static HttpClient CreateClient(string apiUrl, string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "缺少 ApiUrl");
        }

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "缺少登录令牌（请先 --email/--password 登录）");
        }

        var client = new HttpClient
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Authorization = new("Bearer", bearerToken);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("atomupdate-packager/1.0");
        return client;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    /// <summary>上传进度包装流：只读转发底层文件流，读取时上报字节进度（StreamContent 顺序读）。</summary>
    private sealed class ProgressStream(Stream inner, string fileName, long total,
        IProgress<UploadProgress>? progress) : Stream
    {
        private long _transferred;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => _transferred;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Report(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            Report(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Report(read);
            return read;
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Report(int read)
        {
            if (read <= 0 || progress is null)
            {
                return;
            }

            _transferred += read;
            progress.Report(new UploadProgress(fileName, _transferred, total));
        }
    }
}
