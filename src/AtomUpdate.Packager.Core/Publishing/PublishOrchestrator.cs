using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Packager.Core.Api;
using AtomUpdate.Packager.Core.Delta;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Packaging;

namespace AtomUpdate.Packager.Core.Publishing;

/// <summary>发布全链路编排（docs/05 §12）：每架构 full(+可选 delta) → 服务端上传（JWT 登录会话）
/// → 多架构资产合并单请求创建版本（防 409）→ 可选按通道发布。CLI 与 Avalonia 向导共用。</summary>
public sealed class PublishOrchestrator
{
    private readonly IAtomUpdateApiClient _api;
    private readonly IReadOnlyList<IPackager> _packagers;
    private readonly FileDeltaGenerator _deltaGenerator = new();

    public PublishOrchestrator(IAtomUpdateApiClient api, IReadOnlyList<IPackager>? packagers = null)
    {
        _api = api;
        _packagers = packagers ?? new IPackager[] { new ZipPackager(), new InnoPackager() };
    }

    public async Task<PublishResult> ExecuteAsync(PublishPlan plan,
        IProgress<PublishProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidatePlan(plan);

        var packages = new List<PackageResult>();
        var deltas = new List<DeltaResult>();
        var uploads = new List<UploadResult>();
        var assets = new List<ReleaseAssetInputData>();

        foreach (var target in plan.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packager = _packagers.FirstOrDefault(p => p.Supports(target.PackageType))
                ?? throw new PackagerException(PackagerErrorCodes.UnsupportedPackageType,
                    $"不支持的包类型：{target.PackageType}");

            // 1) 全量包
            progress?.Report(new PublishProgress(PublishStage.Package,
                $"打包 {target.Arch.ToString().ToLowerInvariant()} 全量包"));
            var package = await packager.PackageAsync(new PackageContext
            {
                ProjectKey = plan.ProjectKey,
                AppId = plan.AppId,
                Version = plan.Version,
                Platform = plan.Platform,
                Arch = target.Arch,
                PackageType = target.PackageType,
                SourceDirectory = target.SourceDirectory,
                OutputDirectory = plan.OutputDirectory,
                ExcludePatterns = plan.ExcludePatterns,
                SilentArgs = target.SilentArgs,
                InstallArgs = target.InstallArgs,
                InnoSetupPath = plan.InnoSetupPath
            }, cancellationToken);
            packages.Add(package);

            // 2) 增量包（基线目录存在时）
            DeltaResult? delta = null;
            if (!string.IsNullOrEmpty(target.BaselineDirectory))
            {
                progress?.Report(new PublishProgress(PublishStage.Delta,
                    $"生成 {target.Arch.ToString().ToLowerInvariant()} 增量包 {plan.FromVersion} → {plan.Version}"));
                delta = await _deltaGenerator.GenerateAsync(new DeltaContext
                {
                    ProjectKey = plan.ProjectKey,
                    AppId = plan.AppId,
                    FromVersion = plan.FromVersion!,
                    Version = plan.Version,
                    Platform = plan.Platform,
                    Arch = target.Arch,
                    BaselineDirectory = target.BaselineDirectory,
                    CurrentDirectory = target.SourceDirectory,
                    OutputDirectory = plan.OutputDirectory,
                    ExcludePatterns = plan.ExcludePatterns
                }, cancellationToken);
                deltas.Add(delta);
            }

            // 3) 上传（全量 + 增量，服务端存储）
            progress?.Report(new PublishProgress(PublishStage.Upload,
                $"上传 {target.Arch.ToString().ToLowerInvariant()} 全量包"));
            var packageUpload = await _api.UploadAssetAsync(plan.ProjectId, plan.AppNumericId, plan.Version,
                package.PackagePath, BuildUploadProgress(progress, PublishStage.Upload), cancellationToken);
            uploads.Add(packageUpload);
            assets.Add(ReleaseAssetInputData.Full(package, packageUpload));

            if (delta is not null)
            {
                progress?.Report(new PublishProgress(PublishStage.Upload,
                    $"上传 {target.Arch.ToString().ToLowerInvariant()} 增量包"));
                var deltaUpload = await _api.UploadAssetAsync(plan.ProjectId, plan.AppNumericId, plan.Version,
                    delta.PackagePath, BuildUploadProgress(progress, PublishStage.Upload), cancellationToken);
                uploads.Add(deltaUpload);
                assets.Add(ReleaseAssetInputData.Delta(delta, deltaUpload));
            }
        }

        // 4) 合并单请求创建版本
        progress?.Report(new PublishProgress(PublishStage.Push, $"推送版本元数据 {plan.Version}"));
        var request = MetaBuilder.BuildCreate(plan.Version, assets, plan.Notes, plan.ForceUpdate,
            plan.MinSupportedVersion, plan.Status, plan.BuildNumber);
        var release = await _api.CreateReleaseAsync(plan.ProjectId, plan.AppNumericId, request,
            cancellationToken);

        // 5) 可选发布
        var published = false;
        if (plan.PublishAfterCreate && plan.Channels.Count > 0)
        {
            progress?.Report(new PublishProgress(PublishStage.Release,
                $"发布到通道：{string.Join(',', plan.Channels.Select(c => c.ToString().ToLowerInvariant()))}"));
            release = await _api.PublishReleaseAsync(plan.ProjectId, plan.AppNumericId, release.Id,
                MetaBuilder.BuildPublish(plan.Channels), cancellationToken);
            published = true;
        }

        return new PublishResult(release, packages, deltas, uploads, published);
    }

    private static void ValidatePlan(PublishPlan plan)
    {
        if (plan.Targets.Count == 0)
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed, "发布计划至少需要一个架构目标");
        }

        if (plan.Targets.Any(t => !string.IsNullOrEmpty(t.BaselineDirectory)) &&
            string.IsNullOrWhiteSpace(plan.FromVersion))
        {
            throw new PackagerException(PackagerErrorCodes.ApiFailed,
                "存在增量基线目录时必须提供 fromVersion");
        }
    }

    private static Progress<UploadProgress>? BuildUploadProgress(
        IProgress<PublishProgress>? parent, PublishStage stage)
    {
        if (parent is null)
        {
            return null;
        }

        return new Progress<UploadProgress>(p =>
            parent.Report(new PublishProgress(stage, $"{p.FileName} {p.Percent:0.#}%", p.Percent)));
    }
}
