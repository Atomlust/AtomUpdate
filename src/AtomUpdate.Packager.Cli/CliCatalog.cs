namespace AtomUpdate.Packager.Cli;

/// <summary>命令/选项目录（docs/05 §13）：kebab-case 白名单，未知参数报错；布尔标志不消费下一 token。</summary>
public static class CliCatalog
{
    public static readonly string[] Commands = ["publish", "pack", "upload", "list"];

    public const string WorkDir = "work-dir";

    private static readonly string[] PackOptions =
    [
        "project-key", "app-id", "version", "source", "platform", "arch", "type",
        "output", "exclude", "silent-args", "install-args", "inno-setup-path",
        "baseline", "from-version"
    ];

    private static readonly string[] UploadOptions =
        ["file", "api-url", "email", "password", "project", "app", "version"];

    private static readonly string[] ListOptions =
        ["project", "app", "page", "page-size", "api-url", "email", "password"];

    private static readonly string[] PublishOnlyOptions =
    [
        "api-url", "email", "password", "project", "app", "channels",
        "build-number", "status", "min-supported-version",
        "notes-language", "note-new", "note-improvements", "note-fixes", "note", "notes-file"
    ];

    /// <summary>布尔标志（无值）。</summary>
    public static readonly string[] PublishFlags = ["publish", "force-update"];

    public static readonly string[] PackFlags = [];
    public static readonly string[] UploadFlags = [];
    public static readonly string[] ListFlags = [];

    public static readonly IReadOnlyDictionary<string, string[]> ValuedOptions = new Dictionary<string, string[]>
    {
        ["pack"] = PackOptions,
        ["upload"] = UploadOptions,
        ["list"] = ListOptions,
        ["publish"] = [.. PackOptions, .. PublishOnlyOptions]
    };

    public static readonly IReadOnlyDictionary<string, string[]> Flags = new Dictionary<string, string[]>
    {
        ["pack"] = PackFlags,
        ["upload"] = UploadFlags,
        ["list"] = ListFlags,
        ["publish"] = PublishFlags
    };

    // CI secrets 环境变量回退（docs/05 §13）：密钥绝不落日志
    public const string EnvApiUrl = "ATOMUPDATE_PACKAGER_API_URL";
    public const string EnvProject = "ATOMUPDATE_PACKAGER_PROJECT";
    public const string EnvApp = "ATOMUPDATE_PACKAGER_APP";
    public const string EnvEmail = "ATOMUPDATE_PACKAGER_EMAIL";
    public const string EnvPassword = "ATOMUPDATE_PACKAGER_PASSWORD";
    public const string EnvProjectKey = "ATOMUPDATE_PACKAGER_PROJECT_KEY";
    public const string EnvAppId = "ATOMUPDATE_PACKAGER_APP_ID";
}
