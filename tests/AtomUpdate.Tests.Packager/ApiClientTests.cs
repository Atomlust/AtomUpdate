using System.Net;
using System.Text;
using System.Text.Json;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Api;

namespace AtomUpdate.Tests.Packager;

/// <summary>HTTP 发布客户端测试（docs/05 §11）：路由/鉴权头/序列化、201/200、409 幂等冲突、4xx/5xx 处理。</summary>
public sealed class ApiClientTests
{
    private const string BearerToken = "jwt-session-token";

    private static (HttpAtomUpdateApiClient Client, RecordingHandler Handler) CreateClient(
        Func<CapturedRequest, int, Task<(HttpStatusCode Status, string Body)>> responder)
    {
        var handler = new RecordingHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", BearerToken);
        return (new HttpAtomUpdateApiClient(http), handler);
    }

    private static ReleaseCreateRequest SampleCreate() => MetaBuilder.BuildCreate("1.2.0",
    [
        new ReleaseAssetInputData
        {
            Kind = AssetKind.Full, Arch = UpdateArch.X64, PackageType = PackageType.Zip,
            Url = "https://dl.test/x64.zip", Size = 100, Sha256 = new string('a', 64)
        },
        new ReleaseAssetInputData
        {
            Kind = AssetKind.Full, Arch = UpdateArch.Arm64, PackageType = PackageType.Zip,
            Url = "https://dl.test/arm64.zip", Size = 101, Sha256 = new string('b', 64)
        }
    ]);

    private const string ReleaseDtoJson = """
        {
          "id": "1001", "version": "1.2.0", "status": "draft", "forceUpdate": false,
          "publishedAt": null, "createdAt": "2026-10-07T00:00:00+00:00",
          "assets": [
            {"id":"1","kind":"full","fromVersion":null,"arch":"x64","packageType":"zip",
             "url":"https://dl.test/x64.zip","size":100,"sha256":"x","signature":null,"keyId":null,
             "silentArgs":null,"installArgs":null}
          ],
          "notes": null,
          "channels": []
        }
        """;

    [Fact]
    public async Task CreateRelease_PostsCorrectRoute_Header_AndMergedAssets_201()
    {
        var (client, handler) = CreateClient((_, _) =>
            Task.FromResult((HttpStatusCode.Created, ReleaseDtoJson)));

        var dto = await client.CreateReleaseAsync(42, 7, SampleCreate());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/projects/42/apps/7/releases", request.Path);
        Assert.Equal(BearerToken, request.BearerToken);

        using var body = JsonDocument.Parse(request.Body);
        var assets = body.RootElement.GetProperty("assets");
        Assert.Equal(2, assets.GetArrayLength()); // 多架构合并单请求
        Assert.Equal("1.2.0", body.RootElement.GetProperty("version").GetString());
        Assert.False(body.RootElement.TryGetProperty("notes", out _)); // 全空四段不下发

        Assert.Equal(1001, dto.Id);
        Assert.Equal("1.2.0", dto.Version);
        Assert.Equal(ReleaseStatus.Draft, dto.Status);
    }

    [Fact]
    public async Task CreateRelease_Idempotent200_Parses()
    {
        var (client, _) = CreateClient((_, _) =>
            Task.FromResult((HttpStatusCode.OK, ReleaseDtoJson)));

        var dto = await client.CreateReleaseAsync(1, 1, SampleCreate());
        Assert.Equal(1001, dto.Id);
    }

    [Fact]
    public async Task CreateRelease_ConflictReleaseExists_ThrowsDedicatedCode()
    {
        var (client, _) = CreateClient((_, _) =>
            Task.FromResult((HttpStatusCode.Conflict,
                """{"code":"RELEASE_EXISTS","message":"版本 1.2.0 已存在且资产不一致"}""")));

        var exception = await Assert.ThrowsAsync<PackagerException>(
            () => client.CreateReleaseAsync(1, 1, SampleCreate()));
        Assert.Equal(PackagerErrorCodes.ReleaseExists, exception.Code);
    }

    [Fact]
    public async Task CreateRelease_ConflictOtherCode_ThrowsApiFailed()
    {
        var (client, _) = CreateClient((_, _) =>
            Task.FromResult((HttpStatusCode.Conflict,
                """{"code":"CONFLICT","message":"其他冲突"}""")));

        var exception = await Assert.ThrowsAsync<PackagerException>(
            () => client.CreateReleaseAsync(1, 1, SampleCreate()));
        Assert.Equal(PackagerErrorCodes.ApiFailed, exception.Code);
    }

    [Fact]
    public async Task CreateRelease_BadRequest_ThrowsApiFailed()
    {
        var (client, _) = CreateClient((_, _) =>
            Task.FromResult((HttpStatusCode.BadRequest,
                """{"code":"INVALID_REQUEST","message":"版本号非法"}""")));

        var exception = await Assert.ThrowsAsync<PackagerException>(
            () => client.CreateReleaseAsync(1, 1, SampleCreate()));
        Assert.Equal(PackagerErrorCodes.ApiFailed, exception.Code);
        Assert.Contains("INVALID_REQUEST", exception.Message);
    }

    [Fact]
    public async Task CreateRelease_Server500Then200_Retries()
    {
        var (client, handler) = CreateClient((_, callIndex) =>
            Task.FromResult(callIndex == 1
                ? (HttpStatusCode.InternalServerError, "boom")
                : (HttpStatusCode.Created, ReleaseDtoJson)));

        var dto = await client.CreateReleaseAsync(1, 1, SampleCreate());

        Assert.Equal(1001, dto.Id);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PublishRelease_PostsChannels()
    {
        var (client, handler) = CreateClient((_, _) =>
            Task.FromResult((HttpStatusCode.OK, ReleaseDtoJson.Replace("\"draft\"", "\"published\""))));

        var request = MetaBuilder.BuildPublish([UpdateChannel.Stable, UpdateChannel.Beta]);
        await client.PublishReleaseAsync(42, 7, 1001, request);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/api/projects/42/apps/7/releases/1001/publish", sent.Path);
        using var body = JsonDocument.Parse(sent.Body);
        var channels = body.RootElement.GetProperty("channels");
        Assert.Equal(2, channels.GetArrayLength());
        Assert.Equal("stable", channels[1].GetString()); // 枚举 camelCase 字符串
        Assert.False(body.RootElement.TryGetProperty("publishedAt", out _)); // null 省略
    }

    [Fact]
    public async Task ListReleases_GetsPagedRoute()
    {
        var json = """{"items":[],"total":0}""";
        var (client, handler) = CreateClient((_, _) =>
            Task.FromResult((HttpStatusCode.OK, json)));

        var page = await client.ListReleasesAsync(42, 7, page: 2, pageSize: 20);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("/api/projects/42/apps/7/releases?page=2&pageSize=20", sent.Path);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public void Constructor_MissingUrlOrToken_Throws()
    {
        Assert.Throws<PackagerException>(() => new HttpAtomUpdateApiClient(" "));
        Assert.Throws<PackagerException>(() => new HttpAtomUpdateApiClient("http://x/", " "));
    }

    /// <summary>捕获的请求快照。</summary>
    public sealed record CapturedRequest(HttpMethod Method, string Path, string? BearerToken, string Body);

    /// <summary>录制式 HttpMessageHandler：responder 收到 (请求, 从1开始的调用序号)。</summary>
    public sealed class RecordingHandler(
        Func<CapturedRequest, int, Task<(HttpStatusCode Status, string Body)>> responder)
        : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var captured = new CapturedRequest(request.Method, request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.Parameter,
                body);
            Requests.Add(captured);

            var (status, responseBody) = await responder(captured, Requests.Count);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}

/// <summary>MetaBuilder 单测（docs/05 §11）：合并、四段日志、delta 口径。</summary>
public sealed class MetaBuilderTests
{
    private static ReleaseAssetInputData FullAsset(UpdateArch arch) => new()
    {
        Kind = AssetKind.Full, Arch = arch, PackageType = PackageType.Zip,
        Url = $"https://dl.test/{arch}.zip", Size = 10, Sha256 = new string('a', 64)
    };

    [Fact]
    public void BuildCreate_MergesMultiArchAssets_Once()
    {
        var request = MetaBuilder.BuildCreate("2.0.0",
            [FullAsset(UpdateArch.X64), FullAsset(UpdateArch.Arm64)]);

        Assert.Equal(2, request.Assets.Count);
        Assert.Equal("2.0.0", request.Version);
    }

    [Fact]
    public void BuildCreate_EmptyAssets_Throws()
    {
        var exception = Assert.Throws<PackagerException>(() =>
            MetaBuilder.BuildCreate("1.0.0", []));
        Assert.Equal(PackagerErrorCodes.ApiFailed, exception.Code);
    }

    [Fact]
    public void BuildCreate_DeltaWithoutFromVersion_Throws_AndFullWithFromThrows()
    {
        var deltaNoFrom = new ReleaseAssetInputData
        {
            Kind = AssetKind.Delta, Arch = UpdateArch.X64, PackageType = PackageType.Zip,
            Url = "u", Size = 1, Sha256 = new string('a', 64)
        };
        Assert.Throws<PackagerException>(() => MetaBuilder.BuildCreate("1.0.0", [deltaNoFrom]));

        var fullWithFrom = new ReleaseAssetInputData
        {
            Kind = AssetKind.Full, FromVersion = "1.0.0", Arch = UpdateArch.X64,
            PackageType = PackageType.Zip, Url = "u", Size = 1, Sha256 = new string('a', 64)
        };
        Assert.Throws<PackagerException>(() => MetaBuilder.BuildCreate("1.1.0", [fullWithFrom]));
    }

    [Fact]
    public void BuildCreate_DeltaAsset_KeepsFromVersion()
    {
        var delta = new ReleaseAssetInputData
        {
            Kind = AssetKind.Delta, FromVersion = "1.0.0", Arch = UpdateArch.X64,
            PackageType = PackageType.Zip, Url = "u", Size = 1, Sha256 = new string('a', 64)
        };
        var request = MetaBuilder.BuildCreate("1.1.0", [FullAsset(UpdateArch.X64), delta]);

        Assert.Equal(2, request.Assets.Count);
        Assert.Equal("1.0.0", request.Assets[1].FromVersion);
        Assert.Equal(AssetKind.Delta, request.Assets[1].Kind);
    }

    [Fact]
    public void NormalizeNotes_AllEmpty_ReturnsNull()
    {
        Assert.Null(MetaBuilder.NormalizeNotes(new ReleaseNoteInput()));
        Assert.Null(MetaBuilder.NormalizeNotes(null));
        Assert.NotNull(MetaBuilder.NormalizeNotes(new ReleaseNoteInput { RawMarkdown = "# hi" }));
        Assert.NotNull(MetaBuilder.NormalizeNotes(new ReleaseNoteInput { Fixes = ["bug"] }));
    }

    [Fact]
    public void BuildPublish_DeduplicatesOrders_AndGuardsEmpty()
    {
        var request = MetaBuilder.BuildPublish(
            [UpdateChannel.Stable, UpdateChannel.Beta, UpdateChannel.Stable]);
        Assert.Equal([UpdateChannel.Beta, UpdateChannel.Stable], request.Channels);

        Assert.Throws<PackagerException>(() => MetaBuilder.BuildPublish([]));
    }
}
