using AtomUpdate.Updater.Configuration;

namespace AtomUpdate.Updater.Cli;

/// <summary>命令行解析（04 §4.3）：--key value 与 --key=value 两种写法；未知参数计错误（0x1002）。</summary>
public static class CliArguments
{
    public static (CliOptions Options, List<string> Errors) Parse(string[] args)
    {
        var options = new CliOptions();
        var errors = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (!raw.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"无法识别的参数：{raw}");
                continue;
            }

            var body = raw[2..];
            string? inline = null;
            var eq = body.IndexOf('=');
            if (eq >= 0)
            {
                inline = body[(eq + 1)..];
                body = body[..eq];
            }

            string? Value()
            {
                if (inline is not null)
                {
                    return inline;
                }

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return args[++i];
                }

                errors.Add($"参数 --{body} 缺少值");
                return null;
            }

            switch (body.ToLowerInvariant())
            {
                case "config": options.Config = Value(); break;
                case "package": options.Package = Value(); break;
                case "target": options.Target = Value(); break;
                case "from-version": options.FromVersion = Value(); break;
                case "to-version": options.ToVersion = Value(); break;
                case "restart": options.Restart = Value(); break;
                case "restart-args": options.RestartArgs = Value(); break;
                case "sha256": options.Sha256 = Value(); break;
                case "signature": options.Signature = Value(); break;
                case "key-id": options.KeyId = Value(); break;
                case "package-type": options.PackageType = Value(); break;
                case "install-mode": options.InstallMode = Value(); break;
                case "install-args": options.InstallArgs = Value(); break;
                case "replace-old": options.ReplaceOld = Value(); break;
                case "replace-pid":
                    if (int.TryParse(Value(), out var pid))
                    {
                        options.ReplacePid = pid;
                    }
                    else
                    {
                        errors.Add("参数 --replace-pid 必须是整数");
                    }

                    break;
                case "server-url": options.ServerUrl = Value(); break;
                case "installation-id": options.InstallationId = Value(); break;
                case "machine-fingerprint": options.MachineFingerprint = Value(); break;
                case "app-id": options.AppId = Value(); break;
                case "project-key": options.ProjectKey = Value(); break;
                case "log-level": options.LogLevel = Value(); break;
                case "log-path": options.LogPath = Value(); break;
                case "dry-run": options.DryRun = true; break;
                case "no-restart": options.NoRestart = true; break;
                case "no-rollback": options.NoRollback = true; break;
                case "verbose": options.Verbose = true; break;
                case "elevated": options.Elevated = true; break;
                case "help" or "h": options.Help = true; break;
                default: errors.Add($"未知参数：--{body}"); break;
            }
        }

        return (options, errors);
    }

    public static string Usage => """
        AtomUpdate.Updater —— 应用更新安装器（docs/04 §4）

        用法：AtomUpdate.Updater --config <path> [选项]

          --config <path>             任务/配置文件（SDK 经此传入；可与节配置合并）
          --package <path>            更新包路径（必填）
          --target <path>             目标目录（必填）
          --from-version <v>          当前版本
          --to-version <v>            目标版本（必填）
          --package-type <t>          zip|exe|msi|dmg|pkg|appimage|deb|rpm（必填）
          --install-mode <m>          manual|onRestart|immediate
          --sha256 <hash>             期望 SHA256（提供即强制校验）
          --signature <sig> --key-id <id>   包签名与公钥标识（requireSignature 时校验）
          --restart <path>            安装完成后重启的主程序
          --restart-args <args>       重启参数（默认 --skip-update-check）
          --server-url <url>          回传服务器
          --installation-id <id> --machine-fingerprint <fp>
          --app-id <id> --project-key <key> 状态隔离与回传标识
          --install-args <args>       安装器附加参数
          --replace-old <path>        双进程自升级：替换旧更新器后继续任务
          --replace-pid <pid>         等待指定进程退出后再替换
          --log-level <l>             trace|debug|information|warning|error|none
          --log-path <dir>            日志目录（默认平台目录）
          --dry-run                   只校验与演练，不产生替换/回传/重启
          --no-restart                安装后不重启主程序
          --no-rollback               失败时不回滚
          --verbose                   等价 --log-level debug
          --help                      显示本帮助
        """;
}
