namespace AtomUpdate.Packager.Localization;

/// <summary>UI 语言（docs/05 §13）：中英双语，MVP 内置字典，不引资源框架。</summary>
public enum UiLanguage
{
    Zh,
    En
}

/// <summary>中英文字典（docs/05 §13）：键稳定，XAML 以 x:Static 引用；切换语言后重建主窗口生效。</summary>
public static class Strings
{
    public static UiLanguage Language { get; set; } = UiLanguage.Zh;

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["AppTitle"] = "AtomUpdate 打包发布向导",
        ["Step1"] = "1. 目标",
        ["Step2"] = "2. 打包",
        ["Step3"] = "3. 更新日志",
        ["Step4"] = "4. 发布",

        ["LoginSection"] = "账号登录（登录后下拉选择项目与应用，自动填充目标信息）",
        ["LoginEmail"] = "用户名或邮箱",
        ["LoginPassword"] = "密码",
        ["Login"] = "登录",
        ["LoginRunning"] = "登录中…",
        ["LoginOk"] = "✓ 已登录：{0}",
        ["LoginFailed"] = "登录失败：{0}",
        ["SelectProject"] = "项目（下拉选择）",
        ["SelectApp"] = "应用（下拉选择）",
        ["LatestVersion"] = "最新版本：{0}",
        ["PackHint"] = "选择发布目录即可，点击发布时自动完成：打包 zip → 上传 → 推送版本；无需提前手动打包。",
        ["ProjectId"] = "项目数字 ID",
        ["AppId"] = "应用数字 ID",
        ["ProjectKey"] = "项目 Key（包命名）",
        ["AppBusinessId"] = "应用标识（包命名）",
        ["Platform"] = "平台",
        ["Channels"] = "发布通道",

        ["Version"] = "版本号（语义版本）",
        ["BuildNumber"] = "构建号",
        ["SourceDirectory"] = "发布目录",
        ["PackageType"] = "包类型",
        ["Architectures"] = "架构（可多选）",
        ["EnableDelta"] = "同时生成增量包",
        ["BaselineDirectory"] = "基线目录（上一版本发布目录）",
        ["FromVersion"] = "增量起点版本",
        ["ExcludePatterns"] = "排除模式（每行一个 glob）",
        ["OutputDirectory"] = "输出目录",
        ["SilentArgs"] = "静默参数",
        ["InstallArgs"] = "安装参数",

        ["NotesLanguage"] = "日志语言",
        ["NoteNew"] = "新增",
        ["NoteImprovements"] = "改进",
        ["NoteFixes"] = "修复",
        ["NoteNotes"] = "其他说明",
        ["NotesHint"] = "每行一条；四段全空则不下发日志。",

        ["Status"] = "创建状态",
        ["ForceUpdate"] = "强制更新",
        ["MinSupportedVersion"] = "最低支持版本",
        ["PublishNow"] = "推送后立即发布到所选通道",

        ["Back"] = "上一步",
        ["Next"] = "下一步",
        ["Run"] = "开始发布",
        ["Cancel"] = "取消",
        ["ConfirmTitle"] = "发布确认",
        ["ConfirmPublish"] = "确认发布",
        ["Browse"] = "浏览…",
        ["Language"] = "语言",
        ["Theme"] = "主题",
        ["ThemeLight"] = "浅色",
        ["ThemeDark"] = "深色",

        ["ProgressIdle"] = "就绪",
        ["ProgressRunning"] = "执行中…",
        ["ProgressDone"] = "完成",
        ["ProgressCanceled"] = "已取消",
        ["StagePackage"] = "打包",
        ["StageDelta"] = "增量",
        ["StageUpload"] = "上传",
        ["StagePush"] = "推送",
        ["StageRelease"] = "发布",
        ["DraftRestored"] = "已恢复草稿：",
        ["DraftSaved"] = "草稿已自动保存",
        ["ResultTitle"] = "执行结果",
        ["CopyError"] = "复制错误详情",
        ["Copied"] = "已复制"
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["AppTitle"] = "AtomUpdate Packager Wizard",
        ["Step1"] = "1. Target",
        ["Step2"] = "2. Package",
        ["Step3"] = "3. Release Notes",
        ["Step4"] = "4. Publish",

        ["LoginSection"] = "Sign in (pick project & app from dropdowns, auto-fills target info)",
        ["LoginEmail"] = "Username or email",
        ["LoginPassword"] = "Password",
        ["Login"] = "Sign in",
        ["LoginRunning"] = "Signing in…",
        ["LoginOk"] = "✓ Signed in: {0}",
        ["LoginFailed"] = "Sign-in failed: {0}",
        ["SelectProject"] = "Project (dropdown)",
        ["SelectApp"] = "App (dropdown)",
        ["LatestVersion"] = "Latest version: {0}",
        ["PackHint"] = "Just pick the publish output directory — on publish we automatically: pack zip → upload → push release. No manual packing needed.",
        ["ProjectId"] = "Project numeric ID",
        ["AppId"] = "App numeric ID",
        ["ProjectKey"] = "Project key (package naming)",
        ["AppBusinessId"] = "App ID (package naming)",
        ["Platform"] = "Platform",
        ["Channels"] = "Channels",

        ["Version"] = "Version (semver)",
        ["BuildNumber"] = "Build number",
        ["SourceDirectory"] = "Publish directory",
        ["PackageType"] = "Package type",
        ["Architectures"] = "Architectures (multi)",
        ["EnableDelta"] = "Also generate delta packages",
        ["BaselineDirectory"] = "Baseline directory (previous release)",
        ["FromVersion"] = "From version",
        ["ExcludePatterns"] = "Exclude patterns (one glob per line)",
        ["OutputDirectory"] = "Output directory",
        ["SilentArgs"] = "Silent args",
        ["InstallArgs"] = "Install args",

        ["NotesLanguage"] = "Notes language",
        ["NoteNew"] = "New",
        ["NoteImprovements"] = "Improvements",
        ["NoteFixes"] = "Fixes",
        ["NoteNotes"] = "Notes",
        ["NotesHint"] = "One item per line; empty notes are not sent.",

        ["Status"] = "Create status",
        ["ForceUpdate"] = "Force update",
        ["MinSupportedVersion"] = "Min supported version",
        ["PublishNow"] = "Publish to selected channels after push",

        ["Back"] = "Back",
        ["Next"] = "Next",
        ["Run"] = "Run publish",
        ["Cancel"] = "Cancel",
        ["ConfirmTitle"] = "Confirm publish",
        ["ConfirmPublish"] = "Confirm",
        ["Browse"] = "Browse…",
        ["Language"] = "Language",
        ["Theme"] = "Theme",
        ["ThemeLight"] = "Light",
        ["ThemeDark"] = "Dark",

        ["ProgressIdle"] = "Ready",
        ["ProgressRunning"] = "Running…",
        ["ProgressDone"] = "Done",
        ["ProgressCanceled"] = "Canceled",
        ["StagePackage"] = "Package",
        ["StageDelta"] = "Delta",
        ["StageUpload"] = "Upload",
        ["StagePush"] = "Push",
        ["StageRelease"] = "Release",
        ["DraftRestored"] = "Draft restored:",
        ["DraftSaved"] = "Draft autosaved",
        ["ResultTitle"] = "Result",
        ["CopyError"] = "Copy error details",
        ["Copied"] = "Copied"
    };

    public static string T(string key) =>
        (Language == UiLanguage.En ? En : Zh).TryGetValue(key, out var value) ? value : key;

    public static string AppTitle => T("AppTitle");
}
