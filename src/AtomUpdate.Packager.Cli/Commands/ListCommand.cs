using AtomUpdate.Core.Enums;
using AtomUpdate.Packager.Core.Api;

namespace AtomUpdate.Packager.Cli.Commands;

/// <summary>list 子命令（docs/05 §11）：表格输出 id/version/status/archs/channels。</summary>
internal static class ListCommand
{
    public static async Task<int> ExecuteAsync(ParsedCli cli, TextWriter output, CancellationToken cancellationToken)
    {
        var projectId = CliSupport.RequireLong(cli, "project", CliCatalog.EnvProject, "project");
        var appId = CliSupport.RequireLong(cli, "app", CliCatalog.EnvApp, "app");
        var apiUrl = CliSupport.Require(cli, "api-url", CliCatalog.EnvApiUrl);
        var email = CliSupport.Require(cli, "email", CliCatalog.EnvEmail);
        var password = CliSupport.Require(cli, "password", CliCatalog.EnvPassword);
        var page = CliSupport.OptionalInt(cli, "page") ?? 1;
        var pageSize = CliSupport.OptionalInt(cli, "page-size") ?? 50;

        using var manage = new ManageApiClient(apiUrl);
        await manage.LoginAsync(email, password, cancellationToken);
        using var api = new HttpAtomUpdateApiClient(apiUrl, manage.Token);
        var pageResult = await api.ListReleasesAsync(projectId, appId, page, pageSize, cancellationToken);

        await output.WriteLineAsync($"共 {pageResult.Total} 条（第 {page} 页，每页 {pageSize}）");
        await output.WriteLineAsync(
            $"{"ID",-12} {"VERSION",-18} {"STATUS",-12} {"ARCHS",-16} CHANNELS");
        foreach (var release in pageResult.Items)
        {
            var archs = string.Join(',',
                release.Assets.Select(asset => asset.Arch.ToString().ToLowerInvariant()).Distinct().Order());
            var channels = string.Join(',',
                release.Channels
                    .Where(channel => channel.Status == ReleaseChannelStatus.Published)
                    .Select(channel => channel.Channel.ToString().ToLowerInvariant()).Distinct().Order());
            await output.WriteLineAsync(
                $"{release.Id,-12} {release.Version,-18} {release.Status.ToString().ToLowerInvariant(),-12} " +
                $"{archs,-16} {channels}");
        }

        return PackagerExitCodes.Success;
    }
}
