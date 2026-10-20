using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Api;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Publishing;

namespace AtomUpdate.Packager.Cli.Commands;

/// <summary>publish 子命令（docs/05 §5/§13）：PublishOrchestrator 全链路；--publish + --channels 推送后立即发布。</summary>
internal static class PublishCommand
{
    public static async Task<int> ExecuteAsync(ParsedCli cli, TextWriter output, CancellationToken cancellationToken)
    {
        // 命名/布局
        var projectKey = CliSupport.Require(cli, "project-key", CliCatalog.EnvProjectKey);
        var appId = CliSupport.Require(cli, "app-id", CliCatalog.EnvAppId);
        var version = CliSupport.RequireSemVer(cli, "version");
        var source = CliSupport.RequireDirectory(cli, "source");
        var platform = CliSupport.ParseEnum<UpdatePlatform>(CliSupport.Require(cli, "platform"), "platform");
        var packageType = CliSupport.ParseEnum<PackageType>(
            CliSupport.Optional(cli, "type") ?? "zip", "type");

        var archValues = cli.Values("arch");
        var archs = archValues.Count == 0
            ? [UpdateArch.X64]
            : CliSupport.ParseEnumList<UpdateArch>(archValues, "arch");

        var outDir = CliSupport.OptionalDirectory(cli, "output");
        var excludes = cli.Values("exclude");
        var baseline = CliSupport.OptionalDirectory(cli, "baseline");
        var fromVersion = CliSupport.OptionalSemVer(cli, "from-version");
        if ((baseline is null) != (fromVersion is null))
        {
            throw new CliArgumentException("--baseline 与 --from-version 必须同时提供");
        }

        if (baseline is not null && !Directory.Exists(baseline))
        {
            throw new PackagerException(PackagerErrorCodes.DeltaBaselineInvalid, $"基线目录不存在：{baseline}");
        }

        // Api 连接（上传与推送均走 JWT 登录会话）
        var projectId = CliSupport.RequireLong(cli, "project", CliCatalog.EnvProject, "project");
        var appNumericId = CliSupport.RequireLong(cli, "app", CliCatalog.EnvApp, "app");
        var apiUrl = CliSupport.Require(cli, "api-url", CliCatalog.EnvApiUrl);
        var email = CliSupport.Require(cli, "email", CliCatalog.EnvEmail);
        var password = CliSupport.Require(cli, "password", CliCatalog.EnvPassword);

        // 通道/发布（纯参数校验先于任何 IO）
        var channels = CliSupport.ParseEnumList<UpdateChannel>(cli.Values("channels"), "channels");
        var publishNow = cli.Has("publish");
        if (publishNow && channels.Count == 0)
        {
            throw new CliArgumentException("--publish 需要至少一个 --channels");
        }

        if (!publishNow && channels.Count > 0)
        {
            throw new CliArgumentException("提供了 --channels 但缺少 --publish（推送后发布需显式 --publish）");
        }

        ReleaseStatus? status = null;
        var statusRaw = CliSupport.Optional(cli, "status");
        if (statusRaw is not null)
        {
            status = CliSupport.ParseEnum<ReleaseStatus>(statusRaw, "status");
            if (status is ReleaseStatus.Published or ReleaseStatus.Withdrawn or ReleaseStatus.RolledBack)
            {
                throw new CliArgumentException("--status 创建时仅允许 draft/prerelease，发布走 --publish");
            }
        }

        // 存储上传走服务端 Manage 端点，无需本地凭据
        var notes = BuildNotes(cli);
        var targets = archs
            .Select(arch => new PackageTarget
            {
                Arch = arch,
                PackageType = packageType,
                SourceDirectory = source,
                BaselineDirectory = baseline,
                SilentArgs = CliSupport.Optional(cli, "silent-args"),
                InstallArgs = CliSupport.Optional(cli, "install-args")
            })
            .ToList();

        var plan = new PublishPlan
        {
            ProjectKey = projectKey,
            AppId = appId,
            Version = version,
            Platform = platform,
            Targets = targets,
            OutputDirectory = outDir,
            ExcludePatterns = excludes.Count == 0 ? PackageContext.DefaultExcludes : excludes,
            InnoSetupPath = CliSupport.Optional(cli, "inno-setup-path"),
            FromVersion = fromVersion,
            ProjectId = projectId,
            AppNumericId = appNumericId,
            BuildNumber = CliSupport.OptionalInt(cli, "build-number"),
            Status = status,
            ForceUpdate = cli.Has("force-update"),
            MinSupportedVersion = CliSupport.OptionalSemVer(cli, "min-supported-version"),
            Notes = notes,
            PublishAfterCreate = publishNow,
            Channels = channels
        };

        // 登录换取 JWT 会话（上传端点与推送端点共用）
        using var manage = new ManageApiClient(apiUrl);
        await manage.LoginAsync(email, password, cancellationToken);
        using var api = new HttpAtomUpdateApiClient(apiUrl, manage.Token);
        var orchestrator = new PublishOrchestrator(api);

        var lastStage = PublishStage.Package;
        var progress = new Progress<PublishProgress>(p =>
        {
            lastStage = p.Stage;
            var percent = p.Percent is null ? string.Empty : $" {p.Percent.Value,5:F1}%";
            output.WriteLine($"[{StageName(p.Stage)}]{percent} {p.Detail}");
        });

        PublishResult result;
        try
        {
            result = await orchestrator.ExecuteAsync(plan, progress, cancellationToken);
        }
        catch (PackagerException ex) when (lastStage == PublishStage.Release)
        {
            // 版本已创建成功，仅通道发布失败 → 退出码 7（docs/05 §13）
            throw new CliPublishStageException($"通道发布失败（版本 {version} 已创建）：{ex.Message}", ex);
        }

        await output.WriteLineAsync(
            $"RELEASE id={result.Release.Id} version={result.Release.Version} " +
            $"status={result.Release.Status.ToString().ToLowerInvariant()}" +
            (result.Published ? $" channels={string.Join(',', channels.Select(c => c.ToString().ToLowerInvariant()))}" : null));
        foreach (var upload in result.Uploads)
        {
            await output.WriteLineAsync($"ASSET  {upload.Url}");
        }

        return PackagerExitCodes.Success;
    }

    private static ReleaseNoteInput? BuildNotes(ParsedCli cli)
    {
        var rawFile = CliSupport.Optional(cli, "notes-file");
        string? rawMarkdown = null;
        if (rawFile is not null)
        {
            var full = Path.GetFullPath(rawFile);
            if (!File.Exists(full))
            {
                throw new CliArgumentException($"--notes-file 不存在：{full}");
            }

            rawMarkdown = File.ReadAllText(full);
        }

        var notes = new ReleaseNoteInput
        {
            Language = CliSupport.Optional(cli, "notes-language") ?? "en",
            New = cli.Values("note-new"),
            Improvements = cli.Values("note-improvements"),
            Fixes = cli.Values("note-fixes"),
            Notes = cli.Values("note"),
            RawMarkdown = rawMarkdown
        };

        var any = notes.New.Count > 0 || notes.Improvements.Count > 0 || notes.Fixes.Count > 0 ||
            notes.Notes.Count > 0 || !string.IsNullOrWhiteSpace(notes.RawMarkdown);
        return any ? notes : null;
    }

    private static string StageName(PublishStage stage) => stage switch
    {
        PublishStage.Package => "打包",
        PublishStage.Delta => "增量",
        PublishStage.Upload => "上传",
        PublishStage.Push => "推送",
        PublishStage.Release => "发布",
        _ => stage.ToString()
    };
}
