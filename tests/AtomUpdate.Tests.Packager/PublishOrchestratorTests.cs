using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Api;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Publishing;

namespace AtomUpdate.Tests.Packager;

/// <summary>发布编排器全链路离线测试（docs/05 §12）：真实 zip+delta 打包，
/// Api 用伪客户端捕获上传与合并请求（上传直接返回服务端响应口径的 UploadResult）。</summary>
public sealed class PublishOrchestratorTests : IDisposable
{
    private readonly TestWorkspace _ws = new();

    [Fact]
    public async Task ExecuteAsync_FullPlusDeltaUploaded_PushedInOneRequestAndPublished()
    {
        // 目录：基线 app.txt=old → 当前 app.txt=new（delta 有内容差异）
        File.WriteAllText(Path.Combine(_ws.BaselineDirectory, "app.txt"), "old");
        File.WriteAllText(Path.Combine(_ws.SourceDirectory, "app.txt"), "new");

        var plan = new PublishPlan
        {
            ProjectKey = "myproj",
            AppId = "com.example.app",
            Version = "1.2.0",
            Platform = UpdatePlatform.Windows,
            Targets =
            [
                new PackageTarget
                {
                    Arch = UpdateArch.X64,
                    PackageType = PackageType.Zip,
                    SourceDirectory = _ws.SourceDirectory,
                    BaselineDirectory = _ws.BaselineDirectory
                }
            ],
            OutputDirectory = _ws.OutputDirectory,
            FromVersion = "1.1.0",
            ProjectId = 42,
            AppNumericId = 7,
            Status = ReleaseStatus.Draft,
            Notes = new ReleaseNoteInput { Language = "zh", New = ["全新编排器"] },
            PublishAfterCreate = true,
            Channels = [UpdateChannel.Stable]
        };

        var api = new FakeApi();
        var orchestrator = new PublishOrchestrator(api);
        var seenStages = new List<PublishStage>();
        var progress = new Progress<PublishProgress>(p => seenStages.Add(p.Stage));

        var result = await orchestrator.ExecuteAsync(plan, progress);

        // 合并单请求：full + delta 两条资产
        Assert.NotNull(api.Created);
        Assert.Equal("1.2.0", api.Created!.Version);
        Assert.Equal(ReleaseStatus.Draft, api.Created.Status);
        Assert.Equal(2, api.Created.Assets.Count);
        Assert.Equal("zh", api.Created.Notes!.Language);

        var full = Assert.Single(api.Created.Assets, asset => asset.Kind == AssetKind.Full);
        var delta = Assert.Single(api.Created.Assets, asset => asset.Kind == AssetKind.Delta);
        Assert.Equal(UpdateArch.X64, full.Arch);
        Assert.Null(full.FromVersion);
        Assert.Equal("1.1.0", delta.FromVersion);

        // 上传各一次，URL 来自伪服务端响应
        Assert.Equal(2, api.Uploaded.Count);
        Assert.StartsWith("file://", full.Url);
        Assert.StartsWith("file://", delta.Url);
        Assert.False(string.IsNullOrWhiteSpace(full.Sha256));
        Assert.False(string.IsNullOrWhiteSpace(delta.Sha256));

        // 推送后发布被调用
        Assert.NotNull(api.Published);
        Assert.Equal([UpdateChannel.Stable], api.Published!.Channels);
        Assert.True(result.Published);

        // 五个阶段都报过进度（上传发生在打包/增量之后）
        Assert.Contains(PublishStage.Package, seenStages);
        Assert.Contains(PublishStage.Delta, seenStages);
        Assert.Contains(PublishStage.Upload, seenStages);
        Assert.Contains(PublishStage.Push, seenStages);
        Assert.Contains(PublishStage.Release, seenStages);
    }

    [Fact]
    public async Task ExecuteAsync_SingleFull_NoDelta_SkipsPublish()
    {
        File.WriteAllText(Path.Combine(_ws.SourceDirectory, "app.txt"), "v");
        var plan = new PublishPlan
        {
            ProjectKey = "myproj",
            AppId = "com.example.app",
            Version = "2.0.0",
            Platform = UpdatePlatform.Linux,
            Targets =
            [
                new PackageTarget
                {
                    Arch = UpdateArch.Arm64,
                    PackageType = PackageType.Zip,
                    SourceDirectory = _ws.SourceDirectory
                }
            ],
            OutputDirectory = _ws.OutputDirectory,
            ProjectId = 1,
            AppNumericId = 1
        };

        var api = new FakeApi();
        var result = await new PublishOrchestrator(api).ExecuteAsync(plan);

        var asset = Assert.Single(api.Created!.Assets);
        Assert.Equal(AssetKind.Full, asset.Kind);
        Assert.Equal(UpdateArch.Arm64, asset.Arch);
        Assert.Null(asset.FromVersion);
        Assert.Null(api.Published);
        Assert.False(result.Published);
        Assert.Single(api.Uploaded);
        Assert.StartsWith("file://", asset.Url);
        Assert.Empty(result.Deltas);
    }

    [Fact]
    public async Task ExecuteAsync_BaselineWithoutFromVersion_Throws()
    {
        File.WriteAllText(Path.Combine(_ws.SourceDirectory, "a"), "x");
        var plan = new PublishPlan
        {
            ProjectKey = "k", AppId = "a", Version = "1.0.0", Platform = UpdatePlatform.Windows,
            Targets =
            [
                new PackageTarget
                {
                    Arch = UpdateArch.X64, PackageType = PackageType.Zip,
                    SourceDirectory = _ws.SourceDirectory, BaselineDirectory = _ws.BaselineDirectory
                }
            ],
            ProjectId = 1, AppNumericId = 1
        };

        var orchestrator = new PublishOrchestrator(new FakeApi());
        var ex = await Assert.ThrowsAsync<PackagerException>(() => orchestrator.ExecuteAsync(plan));
        Assert.Equal(PackagerErrorCodes.ApiFailed, ex.Code);
    }

    public void Dispose() => _ws.Dispose();

    private sealed class FakeApi : IAtomUpdateApiClient
    {
        public ReleaseCreateRequest? Created { get; private set; }
        public ReleasePublishRequest? Published { get; private set; }
        public List<string> Uploaded { get; } = [];

        public Task<ReleaseDto> CreateReleaseAsync(long projectId, long appId, ReleaseCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            Created = request;
            return Task.FromResult(BuildDto(request, published: false));
        }

        public Task<ReleaseDto> PublishReleaseAsync(long projectId, long appId, long releaseId,
            ReleasePublishRequest request, CancellationToken cancellationToken = default)
        {
            Published = request;
            return Task.FromResult(BuildDto(Created!, published: true, channels: request.Channels));
        }

        public Task<PagedResult<ReleaseDto>> ListReleasesAsync(long projectId, long appId, int page = 1,
            int pageSize = 50, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UploadResult> UploadAssetAsync(long projectId, long appId, string version, string filePath,
            IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var fileName = Path.GetFileName(filePath);
            Uploaded.Add(fileName);
            return Task.FromResult(new UploadResult
            {
                Url = $"file://{fileName}",
                Sha256 = new string('a', 64),
                Size = new FileInfo(filePath).Length,
                Filename = fileName
            });
        }

        private static ReleaseDto BuildDto(ReleaseCreateRequest request, bool published,
            IReadOnlyList<UpdateChannel>? channels = null) => new()
        {
            Id = 9001,
            Version = request.Version,
            BuildNumber = request.BuildNumber,
            Status = request.Status ?? ReleaseStatus.Draft,
            ForceUpdate = request.ForceUpdate,
            CreatedAt = DateTimeOffset.UtcNow,
            Assets = request.Assets.Select((asset, index) => new ReleaseAssetDto
            {
                Id = index + 1,
                Kind = asset.Kind,
                FromVersion = asset.FromVersion,
                Arch = asset.Arch,
                PackageType = asset.PackageType,
                Url = asset.Url,
                Size = asset.Size,
                Sha256 = asset.Sha256
            }).ToList(),
            Channels = (channels ?? []).Select(channel => new ReleaseChannelDto
            {
                Channel = channel,
                Status = ReleaseChannelStatus.Published,
                ReleaseSeq = 1,
                PublishedAt = published ? DateTimeOffset.UtcNow : null
            }).ToList()
        };
    }
}
