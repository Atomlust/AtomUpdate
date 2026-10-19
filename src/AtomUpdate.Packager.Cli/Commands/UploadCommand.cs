using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Api;
using AtomUpdate.Packager.Core.Models;

namespace AtomUpdate.Packager.Cli.Commands;

/// <summary>upload 子命令：登录会话直接上传包文件到服务端存储（{appId}/{version}/{filename}）。</summary>
internal static class UploadCommand
{
    public static async Task<int> ExecuteAsync(ParsedCli cli, TextWriter output, CancellationToken cancellationToken)
    {
        // 纯参数校验先于任何 IO
        var apiUrl = CliSupport.Require(cli, "api-url", CliCatalog.EnvApiUrl);
        var email = CliSupport.Require(cli, "email", CliCatalog.EnvEmail);
        var password = CliSupport.Require(cli, "password", CliCatalog.EnvPassword);
        var projectId = CliSupport.RequireLong(cli, "project", CliCatalog.EnvProject, "project");
        var appNumericId = CliSupport.RequireLong(cli, "app", CliCatalog.EnvApp, "app");
        var version = CliSupport.RequireSemVer(cli, "version");

        var files = cli.Values("file");
        if (files.Count == 0)
        {
            throw new CliArgumentException("缺少必填选项 --file");
        }

        var fullPaths = files.Select(Path.GetFullPath).ToList();
        foreach (var file in fullPaths)
        {
            if (!File.Exists(file))
            {
                throw new PackagerException(PackagerErrorCodes.UploadFailed, $"文件不存在：{file}");
            }
        }

        using var manage = new ManageApiClient(apiUrl);
        await manage.LoginAsync(email, password, cancellationToken);
        using var api = new HttpAtomUpdateApiClient(apiUrl, manage.Token);

        foreach (var file in fullPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteLineAsync($"上传中：{Path.GetFileName(file)}");
            var progress = new Progress<UploadProgress>(p =>
                output.WriteLineAsync($"  {p.Percent,5:F1}%  ({p.BytesTransferred}/{p.TotalBytes})"));
            var result = await api.UploadAssetAsync(projectId, appNumericId, version, file,
                progress, cancellationToken);
            await output.WriteLineAsync($"OK  {result.Url}  sha256={result.Sha256}");
        }

        return PackagerExitCodes.Success;
    }
}
