namespace AtomUpdate.Packager.Cli;

/// <summary>命令行解析（docs/05 §2 偏差：手写解析对齐 Updater/CliArguments.cs 惯例，不引 System.CommandLine）。
/// --key value 与 --key=value 双写法、kebab-case、未知参数报错、可重复选项收集多值、布尔标志无值。</summary>
public static class CliParser
{
    public static ParsedCli Parse(string[] args)
    {
        var errors = new List<string>();
        var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            return New(command: null, help: true, errors, options);
        }

        var command = args[0];
        if (!CliCatalog.Commands.Contains(command))
        {
            errors.Add($"未知命令：{command}（支持：{string.Join("/", CliCatalog.Commands)}）");
            return New(null, false, errors, options);
        }

        var valued = CliCatalog.ValuedOptions[command];
        var flags = CliCatalog.Flags[command];

        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];
            if (IsHelp(token))
            {
                return New(command, help: true, errors, options);
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"无法识别的位置参数：{token}（选项均为 --kebab-case）");
                continue;
            }

            var body = token[2..];
            string name;
            string? inlineValue;
            var eq = body.IndexOf('=');
            if (eq >= 0)
            {
                name = body[..eq];
                inlineValue = body[(eq + 1)..];
            }
            else
            {
                name = body;
                inlineValue = null;
            }

            if (flags.Contains(name))
            {
                if (inlineValue is not null)
                {
                    errors.Add($"布尔标志 --{name} 不接受值");
                }

                Add(options, name, "true");
                continue;
            }

            if (!valued.Contains(name) && name != CliCatalog.WorkDir)
            {
                errors.Add($"命令 {command} 不支持选项：--{name}");
                continue;
            }

            if (inlineValue is not null)
            {
                Add(options, name, inlineValue);
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"选项 --{name} 缺少值");
                continue;
            }

            Add(options, name, args[++i]);
        }

        return New(command, false, errors, options);
    }

    private static void Add(Dictionary<string, List<string>> options, string name, string value)
    {
        if (!options.TryGetValue(name, out var list))
        {
            list = [];
            options[name] = list;
        }

        list.Add(value);
    }

    private static bool IsHelp(string token) => token is "-h" or "--help";

    private static ParsedCli New(string? command, bool help, List<string> errors,
        Dictionary<string, List<string>> options) => new()
    {
        Command = command,
        HelpRequested = help,
        Errors = errors,
        Options = options.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal)
    };

    public static string Usage => """
        AtomUpdate.Packager —— 打包发布工具（docs/05）

        用法：atomupdate-packager <命令> [选项]

          publish   打包 → 增量 → 上传（服务端）→ 推送（→ 发布）全链路
          pack      仅打包（zip 全量/增量；Windows 可打 exe）
          upload    上传本地包文件到服务端存储
          list      列出应用的版本

        通用：
          --work-dir <dir>       工作目录（草稿；缺省 %LocalAppData%/atomupdate/packager）
          -h|--help              查看帮助

        pack 选项：
          --project-key <key>    项目 key（包命名）
          --app-id <id>         应用标识（包命名/远端路径）
          --version <ver>       语义版本（如 1.2.0）
          --source <dir>        发布目录
          --platform <p>        windows/macos/linux/ios/android/harmony
          --arch <a>            x64/arm64/x86（可重复）
          --type <t>            zip/exe（缺省 zip）
          --output <dir>        输出目录（缺省发布目录同级 packs/）
          --exclude <glob>      排除模式（可重复）
          --from-version <ver>  增量起点版本（配合 --baseline）
          --baseline <dir>      上一版本发布目录（给出则同时产增量包）
          --silent-args, --install-args, --inno-setup-path

        upload 选项：
          --api-url <url>       服务端地址
          --email, --password   登录账号（上传走 JWT 登录会话）
          --project <id>        项目数字 ID
          --app <id>            应用数字 ID
          --version <ver>       语义版本
          --file <path>         本地包文件（可重复）

        list 选项：
          --project <id>        项目数字 ID
          --app <id>            应用数字 ID
          --page, --page-size, --api-url, --email, --password

        publish 选项：pack 全部选项 +
          --project <id>, --app <id>           Api 数字 ID
          --api-url <url>                      服务端地址
          --email, --password                  登录账号（上传与推送共用登录会话）
          --channels <c>                       stable/beta/rc/preview/dev（可重复）
          --publish                            推送后立即发布到 --channels
          --build-number, --status <draft|prerelease>, --force-update, --min-supported-version
          --notes-language, --note-new, --note-improvements, --note-fixes, --note, --notes-file

        环境变量（CI 回退，命令行优先）：
          ATOMUPDATE_PACKAGER_API_URL / _API_KEY / _PROJECT / _APP
          ATOMUPDATE_PACKAGER_EMAIL / _PASSWORD
          ATOMUPDATE_PACKAGER_PROJECT_KEY / _APP_ID

        示例：
          atomupdate-packager publish --project-key myproj --app-id com.example.app \
            --project 42 --app 7 --version 1.2.0 --source ./publish/win-x64 \
            --platform windows --arch x64 --arch arm64 \
            --api-url https://api.example.com --email admin@example.com \
            --publish --channels stable
        """;
}
