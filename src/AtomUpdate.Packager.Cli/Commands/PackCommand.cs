using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Delta;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Packaging;

namespace AtomUpdate.Packager.Cli.Commands;

/// <summary>pack 子命令（docs/05 §6/§7）：按架构打全量包；--baseline + --from-version 时同时产增量包。</summary>
internal static class PackCommand
{
    public static async Task<int> ExecuteAsync(ParsedCli cli, TextWriter output, CancellationToken cancellationToken)
    {
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
        var excludePatterns = excludes.Count == 0
            ? PackageContext.DefaultExcludes
            : excludes;
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

        IPackager packager = packageType switch
        {
            PackageType.Zip => new ZipPackager(),
            PackageType.Exe => new InnoPackager(),
            _ => throw new PackagerException(PackagerErrorCodes.UnsupportedPackageType,
                $"pack 仅支持 zip/exe，实际：{packageType.ToString().ToLowerInvariant()}")
        };

        var deltaGenerator = new FileDeltaGenerator();
        var silentArgs = CliSupport.Optional(cli, "silent-args");
        var installArgs = CliSupport.Optional(cli, "install-args");
        var innoSetupPath = CliSupport.Optional(cli, "inno-setup-path");

        foreach (var arch in archs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = await packager.PackageAsync(new PackageContext
            {
                ProjectKey = projectKey,
                AppId = appId,
                Version = version,
                Platform = platform,
                Arch = arch,
                PackageType = packageType,
                SourceDirectory = source,
                OutputDirectory = outDir,
                ExcludePatterns = excludePatterns,
                SilentArgs = silentArgs,
                InstallArgs = installArgs,
                InnoSetupPath = innoSetupPath
            }, cancellationToken);
            await output.WriteLineAsync(
                $"FULL  {FormatTag(platform, arch)}  {package.FileName}  {package.Sha256}  {package.Size} 字节");

            if (baseline is not null)
            {
                var delta = await deltaGenerator.GenerateAsync(new DeltaContext
                {
                    ProjectKey = projectKey,
                    AppId = appId,
                    FromVersion = fromVersion!,
                    Version = version,
                    Platform = platform,
                    Arch = arch,
                    BaselineDirectory = baseline,
                    CurrentDirectory = source,
                    OutputDirectory = outDir,
                    ExcludePatterns = excludePatterns
                }, cancellationToken);
                await output.WriteLineAsync(
                    $"DELTA {FormatTag(platform, arch)}  {delta.FileName}  {delta.Sha256}  {delta.Size} 字节" +
                    $"（{delta.FileCount} 变更 / {delta.DeletedCount} 删除）");
            }
        }

        return PackagerExitCodes.Success;
    }

    private static string FormatTag(UpdatePlatform platform, UpdateArch arch) =>
        $"{platform.ToString().ToLowerInvariant()}-{arch.ToString().ToLowerInvariant()}";
}
