using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using FluentValidation;
using NuGet.Versioning;

namespace AtomUpdate.Validation.Manage;

public sealed class ReleaseCreateRequestValidator : AbstractValidator<ReleaseCreateRequest>
{
    public ReleaseCreateRequestValidator()
    {
        RuleFor(x => x.Version)
            .NotEmpty().WithMessage("version 不能为空")
            .MaximumLength(100)
            .Must(v => SemanticVersion.TryParse(v, out _))
            .WithMessage("version 必须是合法的语义化版本号（如 1.2.3）");

        RuleFor(x => x.BuildNumber)
            .GreaterThanOrEqualTo(0).WithMessage("buildNumber 不能为负数")
            .When(x => x.BuildNumber is not null);

        RuleFor(x => x.Status)
            .Must(s => s is null or ReleaseStatus.Draft or ReleaseStatus.Prerelease)
            .WithMessage("status 仅允许 draft 或 prerelease（发布走 publish 端点）");

        RuleFor(x => x.MinSupportedVersion)
            .Must(v => v is not null && SemanticVersion.TryParse(v, out _))
            .WithMessage("minSupportedVersion 必须是合法的语义化版本号")
            .When(x => !string.IsNullOrEmpty(x.MinSupportedVersion));

        RuleFor(x => x.Assets)
            .NotEmpty().WithMessage("assets 不能为空")
            .Must(a => a.Count <= 64).WithMessage("assets 数量过多（最多 64 个）")
            .Must(a => a.Any(x => x.Kind == AssetKind.Full))
            .WithMessage("必须至少包含一个 full 资产（fullPackage 兜底所需）");

        RuleForEach(x => x.Assets).SetValidator(new ReleaseAssetInputValidator());

        RuleFor(x => x.Assets)
            .Must(a => a.GroupBy(x => (x.Kind, x.FromVersion, x.PackageType, x.Arch)).All(g => g.Count() == 1))
            .WithMessage("assets 中 (type, fromVersion, packageType, arch) 组合不能重复");

        RuleFor(x => x.Notes!).SetValidator(new ReleaseNoteInputValidator()).When(x => x.Notes is not null);
    }
}

public sealed class ReleaseAssetInputValidator : AbstractValidator<ReleaseAssetInput>
{
    public ReleaseAssetInputValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("type 取值非法");
        RuleFor(x => x.Arch).IsInEnum().WithMessage("arch 取值非法");
        RuleFor(x => x.PackageType).IsInEnum().WithMessage("packageType 取值非法");

        RuleFor(x => x.FromVersion)
            .Null().WithMessage("full 资产不允许携带 fromVersion")
            .When(x => x.Kind == AssetKind.Full);

        RuleFor(x => x.FromVersion)
            .NotEmpty().WithMessage("delta 资产必须携带 fromVersion")
            .Must(v => v is not null && SemanticVersion.TryParse(v, out _))
            .WithMessage("fromVersion 必须是合法的语义化版本号")
            .When(x => x.Kind == AssetKind.Delta);

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("url 不能为空")
            .MaximumLength(2000)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
                       (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("url 必须是 http/https 绝对地址");

        RuleFor(x => x.Sha256)
            .NotEmpty().WithMessage("sha256 不能为空")
            .Matches("^[0-9a-fA-F]{64}$").WithMessage("sha256 必须是 64 位十六进制");

        RuleFor(x => x.Size)
            .GreaterThanOrEqualTo(0).WithMessage("size 不能为负数")
            .When(x => x.Size is not null);
    }
}

public sealed class ReleaseNoteInputValidator : AbstractValidator<ReleaseNoteInput>
{
    public ReleaseNoteInputValidator()
    {
        RuleFor(x => x.Language).NotEmpty().WithMessage("notes.language 不能为空").MaximumLength(20);
        RuleFor(x => x.New).Must(l => l.Count <= 100).WithMessage("notes.new 条目过多（最多 100 条）");
        RuleFor(x => x.Improvements).Must(l => l.Count <= 100).WithMessage("notes.improvements 条目过多（最多 100 条）");
        RuleFor(x => x.Fixes).Must(l => l.Count <= 100).WithMessage("notes.fixes 条目过多（最多 100 条）");
        RuleFor(x => x.Notes).Must(l => l.Count <= 100).WithMessage("notes.notes 条目过多（最多 100 条）");
        RuleFor(x => x.RawMarkdown).MaximumLength(20000);
    }
}

public sealed class ReleasePublishRequestValidator : AbstractValidator<ReleasePublishRequest>
{
    public ReleasePublishRequestValidator()
    {
        RuleFor(x => x.Channels)
            .NotEmpty().WithMessage("channels 不能为空")
            .Must(c => c.Distinct().Count() == c.Count)
            .WithMessage("channels 不能重复");

        RuleForEach(x => x.Channels).IsInEnum().WithMessage("channel 取值非法");
    }
}

public sealed class ReleaseUpdateRequestValidator : AbstractValidator<ReleaseUpdateRequest>
{
    public ReleaseUpdateRequestValidator()
    {
        RuleFor(x => x.BuildNumber)
            .GreaterThanOrEqualTo(0).WithMessage("buildNumber 不能为负数")
            .When(x => x.BuildNumber is not null);

        RuleFor(x => x.MinSupportedVersion)
            .Must(v => v is not null && SemanticVersion.TryParse(v, out _))
            .WithMessage("minSupportedVersion 必须是合法的语义化版本号")
            .When(x => !string.IsNullOrEmpty(x.MinSupportedVersion));

        RuleFor(x => x.Notes!).SetValidator(new ReleaseNoteInputValidator()).When(x => x.Notes is not null);
    }
}
