using System.Collections.ObjectModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models.Manage;
using AtomUpdate.Packager.Core;
using AtomUpdate.Packager.Core.Api;
using AtomUpdate.Packager.Core.Models;
using AtomUpdate.Packager.Core.Publishing;
using AtomUpdate.Packager.Localization;
using AtomUpdate.Packager.Models;
using AtomUpdate.Packager.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NuGet.Versioning;

namespace AtomUpdate.Packager.ViewModels;

/// <summary>四步向导状态机（docs/05 §4/§12）：目标 → 打包 → 日志 → 发布；
/// 步间校验、草稿自动保存、后台执行 PublishOrchestrator（IProgress 回 UI 线程，取消在阶段边界生效）。</summary>
public partial class WizardViewModel : ViewModelBase
{
    private readonly PackagerPaths _paths;
    private readonly WizardDraft _draft;
    private CancellationTokenSource? _cts;

    public WizardViewModel() : this(new PackagerPaths(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "atomupdate", "packager")))
    {
    }

    public WizardViewModel(PackagerPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
        _draft = WizardDraft.LoadLatest(_paths) ?? new WizardDraft();
        RestoreDraft(_draft);
    }

    // ---------------- Step1 目标 ----------------

    [ObservableProperty] public partial string ProjectId { get; set; } = string.Empty;
    [ObservableProperty] public partial string AppId { get; set; } = string.Empty;
    [ObservableProperty] public partial string ProjectKey { get; set; } = string.Empty;
    [ObservableProperty] public partial string AppBusinessId { get; set; } = string.Empty;
    [ObservableProperty] public partial string PlatformSelection { get; set; } = "windows";
    [ObservableProperty] public partial bool ChannelStable { get; set; }
    [ObservableProperty] public partial bool ChannelBeta { get; set; }
    [ObservableProperty] public partial bool ChannelRc { get; set; }
    [ObservableProperty] public partial bool ChannelPreview { get; set; }
    [ObservableProperty] public partial bool ChannelDev { get; set; }

    // ---------------- Step1 登录与远程选择（登录后下拉选择项目/应用，自动填充目标信息） ----------------

    [ObservableProperty] public partial string LoginServerUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string LoginEmail { get; set; } = string.Empty;
    [ObservableProperty] public partial string LoginPassword { get; set; } = string.Empty;
    [ObservableProperty] public partial string LoginStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial ProjectDto? SelectedProject { get; set; }
    [ObservableProperty] public partial AppDto? SelectedApp { get; set; }
    [ObservableProperty] public partial string LatestVersionText { get; set; } = string.Empty;

    public ObservableCollection<ProjectDto> Projects { get; } = [];
    public ObservableCollection<AppDto> Apps { get; } = [];

    /// <summary>登录未选应用时显示手动目标区（数字 ID / 业务标识 / 平台）；选中应用后自动填充并隐藏，避免冗余输入。</summary>
    public bool ManualTargetVisible => SelectedApp is null;

    private ManageApiClient? _manageClient;

    [RelayCommand]
    private async Task LoginAsync()
    {
        LoginStatus = Strings.T("LoginRunning");
        try
        {
            _manageClient?.Dispose();
            _manageClient = new ManageApiClient(LoginServerUrl);
            var email = await _manageClient.LoginAsync(LoginEmail, LoginPassword);
            LoginStatus = string.Format(Strings.T("LoginOk"), email);

            Projects.Clear();
            foreach (var project in await _manageClient.ListProjectsAsync())
            {
                Projects.Add(project);
            }
        }
        catch (PackagerException ex)
        {
            LoginStatus = ex.Message;
        }
        catch (Exception ex)
        {
            LoginStatus = string.Format(Strings.T("LoginFailed"), ex.Message);
        }
    }

    partial void OnSelectedProjectChanged(ProjectDto? value)
    {
        Apps.Clear();
        SelectedApp = null;
        LatestVersionText = string.Empty;
        OnPropertyChanged(nameof(ManualTargetVisible));
        if (value is not null && _manageClient is not null)
        {
            _ = LoadAppsAsync(value);
        }
    }

    private async Task LoadAppsAsync(ProjectDto project)
    {
        try
        {
            var apps = await _manageClient!.ListAppsAsync(project.Id);
            Apps.Clear();
            foreach (var app in apps)
            {
                Apps.Add(app);
            }
        }
        catch (PackagerException ex)
        {
            LoginStatus = ex.Message;
        }
    }

    partial void OnSelectedAppChanged(AppDto? value)
    {
        OnPropertyChanged(nameof(ManualTargetVisible));
        if (SelectedProject is { } project && value is { } app)
        {
            // 自动填充目标信息，替代手输数字 ID；Step1 校验逻辑保持不变
            ProjectId = project.Id.ToString();
            AppId = app.Id.ToString();
            ProjectKey = project.Key;
            AppBusinessId = app.AppId;
            PlatformSelection = MapPlatform(app.Platform);
            _ = LoadLatestVersionAsync(project, app);
        }
        else
        {
            LatestVersionText = string.Empty;
        }
    }

    private async Task LoadLatestVersionAsync(ProjectDto project, AppDto app)
    {
        try
        {
            var release = await _manageClient!.ListLatestReleaseAsync(project.Id, app.Id);
            LatestVersionText = release is null
                ? string.Empty
                : string.Format(Strings.T("LatestVersion"), release.Version);
        }
        catch (Exception)
        {
            // 静默失败：最新版本号仅作展示提示，不阻塞发布流程
            LatestVersionText = string.Empty;
        }
    }

    private static string MapPlatform(UpdatePlatform platform) => platform switch
    {
        UpdatePlatform.Macos => "macos",
        UpdatePlatform.HarmonyOS => "harmony",
        _ => platform.ToString().ToLowerInvariant()
    };

    // ---------------- Step2 打包 ----------------

    [ObservableProperty] public partial string Version { get; set; } = string.Empty;
    [ObservableProperty] public partial string BuildNumber { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceDirectory { get; set; } = string.Empty;
    [ObservableProperty] public partial string PackageTypeSelection { get; set; } = "zip";
    [ObservableProperty] public partial bool ArchX64 { get; set; } = true;
    [ObservableProperty] public partial bool ArchArm64 { get; set; }
    [ObservableProperty] public partial bool ArchX86 { get; set; }
    [ObservableProperty] public partial bool EnableDelta { get; set; }
    [ObservableProperty] public partial string BaselineDirectory { get; set; } = string.Empty;
    [ObservableProperty] public partial string FromVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExcludePatternsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string OutputDirectory { get; set; } = string.Empty;
    [ObservableProperty] public partial string SilentArgs { get; set; } = string.Empty;
    [ObservableProperty] public partial string InstallArgs { get; set; } = string.Empty;

    // ---------------- Step3 日志 ----------------

    [ObservableProperty] public partial string NotesLanguage { get; set; } = "en";
    [ObservableProperty] public partial string NoteNewText { get; set; } = string.Empty;
    [ObservableProperty] public partial string NoteImprovementsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string NoteFixesText { get; set; } = string.Empty;
    [ObservableProperty] public partial string NoteNotesText { get; set; } = string.Empty;
    [ObservableProperty] public partial string NoteRawMarkdown { get; set; } = string.Empty;

    // ---------------- Step4 发布 ----------------

    [ObservableProperty] public partial string StatusSelection { get; set; } = "draft";
    [ObservableProperty] public partial bool ForceUpdate { get; set; }
    [ObservableProperty] public partial string MinSupportedVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial bool PublishNow { get; set; }

    // ---------------- 向导状态 ----------------

    [ObservableProperty] public partial int CurrentStep { get; set; }
    [ObservableProperty] public partial string StepError { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial bool IsConfirming { get; set; }
    [ObservableProperty] public partial string StageText { get; set; } = Strings.T("ProgressIdle");
    [ObservableProperty] public partial string DetailText { get; set; } = string.Empty;
    [ObservableProperty] public partial double? Percent { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = string.Empty;
    [ObservableProperty] public partial string ConfirmSummary { get; set; } = string.Empty;
    [ObservableProperty] public partial string ErrorSummary { get; set; } = string.Empty;
    [ObservableProperty] public partial string ErrorDetail { get; set; } = string.Empty;
    [ObservableProperty] public partial string DraftBanner { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyStatus { get; set; } = string.Empty;

    public bool IsLastStep => CurrentStep == 3;
    public bool CanGoBack => CurrentStep > 0 && !IsRunning;
    public bool Step0Visible => CurrentStep == 0;
    public bool Step1Visible => CurrentStep == 1;
    public bool Step2Visible => CurrentStep == 2;
    public bool Step3Visible => CurrentStep == 3;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(Step0Visible));
        OnPropertyChanged(nameof(Step1Visible));
        OnPropertyChanged(nameof(Step2Visible));
        OnPropertyChanged(nameof(Step3Visible));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        NextCommand.NotifyCanExecuteChanged();
        RequestRunCommand.NotifyCanExecuteChanged();
        CancelRunCommand.NotifyCanExecuteChanged();
    }

    // ---------------- 步骤流转 ----------------

    [RelayCommand]
    private void Next()
    {
        if (IsRunning)
        {
            return;
        }

        var error = ValidateStep(CurrentStep);
        if (error is not null)
        {
            StepError = error;
            return;
        }

        StepError = string.Empty;
        CurrentStep = Math.Min(CurrentStep + 1, 3);
        SaveDraft();
    }

    [RelayCommand]
    private void Back()
    {
        if (IsRunning)
        {
            return;
        }

        StepError = string.Empty;
        CurrentStep = Math.Max(CurrentStep - 1, 0);
        SaveDraft();
    }

    private string? ValidateStep(int step)
    {
        switch (step)
        {
            case 0:
            {
                if (!long.TryParse(ProjectId, out var pid) || pid <= 0)
                {
                    return Strings.T("ProjectId") + " → positive integer";
                }

                if (!long.TryParse(AppId, out var aid) || aid <= 0)
                {
                    return Strings.T("AppId") + " → positive integer";
                }

                if (string.IsNullOrWhiteSpace(ProjectKey) || string.IsNullOrWhiteSpace(AppBusinessId))
                {
                    return "Project key / App ID required";
                }

                if (!Uri.TryCreate(LoginServerUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                {
                    return "API URL must be an absolute http(s) URL";
                }

                if (_manageClient?.IsLoggedIn != true)
                {
                    return "Sign-in required (publish uses the login session)";
                }

                return null;
            }

            case 1:
            {
                if (!SemanticVersion.TryParse(Version, out _))
                {
                    return "Version is not valid semver";
                }

                if (!string.IsNullOrWhiteSpace(BuildNumber) &&
                    (!int.TryParse(BuildNumber, out var build) || build <= 0))
                {
                    return "Build number must be a positive integer";
                }

                if (!Directory.Exists(SourceDirectory))
                {
                    return "Source directory does not exist";
                }

                if (!AnyArch())
                {
                    return "Select at least one architecture";
                }

                if (EnableDelta)
                {
                    if (!SemanticVersion.TryParse(FromVersion, out _))
                    {
                        return "Delta from-version is not valid semver";
                    }

                    if (!Directory.Exists(BaselineDirectory))
                    {
                        return "Baseline directory does not exist";
                    }
                }

                if (!string.IsNullOrWhiteSpace(OutputDirectory) &&
                    !Directory.Exists(OutputDirectory))
                {
                    return "Output directory does not exist";
                }

                return null;
            }

            case 2:
                return null;

            case 3:
            {
                if (!string.IsNullOrWhiteSpace(MinSupportedVersion) &&
                    !SemanticVersion.TryParse(MinSupportedVersion, out _))
                {
                    return "Min supported version is not valid semver";
                }

                if (PublishNow && SelectedChannels().Count == 0)
                {
                    return "Publishing requires at least one channel";
                }

                return null;
            }

            default:
                return null;
        }
    }

    private bool AnyArch() => ArchX64 || ArchArm64 || ArchX86;

    private List<UpdateArch> SelectedArchitectures()
    {
        var list = new List<UpdateArch>(3);
        if (ArchX86) list.Add(UpdateArch.X86);
        if (ArchX64) list.Add(UpdateArch.X64);
        if (ArchArm64) list.Add(UpdateArch.Arm64);
        return list;
    }

    private List<UpdateChannel> SelectedChannels()
    {
        var list = new List<UpdateChannel>(5);
        if (ChannelStable) list.Add(UpdateChannel.Stable);
        if (ChannelBeta) list.Add(UpdateChannel.Beta);
        if (ChannelRc) list.Add(UpdateChannel.Rc);
        if (ChannelPreview) list.Add(UpdateChannel.Preview);
        if (ChannelDev) list.Add(UpdateChannel.Dev);
        return list;
    }

    // ---------------- 窗口辅助 ----------------

    private static Window? OwnerWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    // ---------------- 执行 ----------------

    [RelayCommand(CanExecute = nameof(CanRequestRun))]
    private void RequestRun()
    {
        for (var step = 0; step < 4; step++)
        {
            var error = ValidateStep(step);
            if (error is not null)
            {
                CurrentStep = step;
                StepError = error;
                return;
            }
        }

        StepError = string.Empty;
        ErrorSummary = string.Empty;
        ErrorDetail = string.Empty;
        if (PublishNow)
        {
            var archs = string.Join('/', SelectedArchitectures().Select(a => a.ToString().ToLowerInvariant()));
            var channels = string.Join('/', SelectedChannels().Select(c => c.ToString().ToLowerInvariant()));
            ConfirmSummary =
                $"version {Version} ({PlatformSelection}-{archs})\n" +
                $"project={ProjectId} app={AppId}\n" +
                $"channels={channels}";
            IsConfirming = true;
            return;
        }

        _ = RunAsync();
    }

    private bool CanRequestRun() => !IsRunning;

    [RelayCommand]
    private void ConfirmRun()
    {
        IsConfirming = false;
        _ = RunAsync();
    }

    [RelayCommand]
    private void CancelConfirmation() => IsConfirming = false;

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void CancelRun() => _cts?.Cancel();

    private bool CanCancelRun() => IsRunning;

    [RelayCommand]
    private async Task CopyErrorAsync()
    {
        if (OwnerWindow()?.Clipboard is { } clipboard && !string.IsNullOrEmpty(ErrorDetail))
        {
            await clipboard.SetTextAsync(ErrorDetail);
            CopyStatus = Strings.T("Copied");
        }
    }

    private async Task RunAsync()
    {
        var targets = SelectedArchitectures()
            .Select(arch => new PackageTarget
            {
                Arch = arch,
                PackageType = ParseEnum<PackageType>(PackageTypeSelection),
                SourceDirectory = SourceDirectory,
                BaselineDirectory = EnableDelta ? BaselineDirectory : null,
                SilentArgs = NullIfEmpty(SilentArgs),
                InstallArgs = NullIfEmpty(InstallArgs)
            })
            .ToList();

        ReleaseNoteInput? notes = BuildNotes();

        var plan = new PublishPlan
        {
            ProjectKey = ProjectKey.Trim(),
            AppId = AppBusinessId.Trim(),
            Version = Version.Trim(),
            Platform = ParseEnum<UpdatePlatform>(PlatformSelection),
            Targets = targets,
            OutputDirectory = NullIfEmpty(OutputDirectory),
            ExcludePatterns = ParseLines(ExcludePatternsText) is { Count: > 0 } excludes
                ? excludes
                : PackageContext.DefaultExcludes,
            FromVersion = EnableDelta ? FromVersion.Trim() : null,
            ProjectId = long.Parse(ProjectId),
            AppNumericId = long.Parse(AppId),
            BuildNumber = int.TryParse(BuildNumber, out var build) ? build : null,
            Status = ParseEnum<ReleaseStatus>(StatusSelection),
            ForceUpdate = ForceUpdate,
            MinSupportedVersion = NullIfEmpty(MinSupportedVersion),
            Notes = notes,
            PublishAfterCreate = PublishNow,
            Channels = SelectedChannels()
        };

        var api = new HttpAtomUpdateApiClient(LoginServerUrl.Trim(), _manageClient?.Token);
        var orchestrator = new PublishOrchestrator(api);
        var progress = new Progress<PublishProgress>(p =>
        {
            StageText = StageName(p.Stage);
            DetailText = p.Detail;
            Percent = p.Percent;
        });

        _cts = new CancellationTokenSource();
        IsRunning = true;
        ResultSummary = string.Empty;
        try
        {
            var result = await Task.Run(
                () => orchestrator.ExecuteAsync(plan, progress, _cts.Token), _cts.Token);
            StageText = Strings.T("ProgressDone");
            Percent = 100;
            var channels = result.Published
                ? " channels=" + string.Join(',', SelectedChannels().Select(c => c.ToString().ToLowerInvariant()))
                : null;
            ResultSummary =
                $"RELEASE id={result.Release.Id} version={result.Release.Version} " +
                $"status={result.Release.Status.ToString().ToLowerInvariant()}{channels}\n" +
                string.Join('\n', result.Uploads.Select(u => $"ASSET  {u.Url}"));
            SaveDraft();
        }
        catch (OperationCanceledException)
        {
            StageText = Strings.T("ProgressCanceled");
        }
        catch (PackagerException ex)
        {
            ShowError(ex);
        }
        catch (Exception ex)
        {
            ErrorSummary = ex.Message;
            ErrorDetail = ex.ToString();
            StageText = ex.GetType().Name;
        }
        finally
        {
            IsRunning = false;
            api.Dispose();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static TEnum ParseEnum<TEnum>(string raw) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : throw new InvalidOperationException($"Bad enum value: {raw}");

    private ReleaseNoteInput? BuildNotes()
    {
        var sections = new[]
        {
            NoteNewText, NoteImprovementsText, NoteFixesText, NoteNotesText
        };
        var raw = NullIfEmpty(NoteRawMarkdown);
        if (sections.All(string.IsNullOrWhiteSpace) && raw is null)
        {
            return null;
        }

        return new ReleaseNoteInput
        {
            Language = string.IsNullOrWhiteSpace(NotesLanguage) ? "en" : NotesLanguage.Trim(),
            New = ParseLines(NoteNewText),
            Improvements = ParseLines(NoteImprovementsText),
            Fixes = ParseLines(NoteFixesText),
            Notes = ParseLines(NoteNotesText),
            RawMarkdown = raw
        };
    }

    private static List<string> ParseLines(string text) =>
        text.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string StageName(PublishStage stage) => Strings.T(stage switch
    {
        PublishStage.Package => "StagePackage",
        PublishStage.Delta => "StageDelta",
        PublishStage.Upload => "StageUpload",
        PublishStage.Push => "StagePush",
        PublishStage.Release => "StageRelease",
        _ => stage.ToString()
    });

    private void ShowError(PackagerException ex)
    {
        ErrorSummary = $"[{ex.Code}] {ex.Message}";
        ErrorDetail = ex.ToString();
        StageText = ex.Code;
    }

    // ---------------- 草稿 ----------------

    private void SaveDraft()
    {
        _draft.ProjectId = ProjectId;
        _draft.AppId = AppId;
        _draft.ProjectKey = ProjectKey;
        _draft.AppBusinessId = AppBusinessId;
        _draft.Platform = PlatformSelection;
        _draft.Channels = SelectedChannels().Select(c => c.ToString()).ToList();

        _draft.Version = Version;
        _draft.BuildNumber = BuildNumber;
        _draft.SourceDirectory = SourceDirectory;
        _draft.PackageType = PackageTypeSelection;
        _draft.Architectures = SelectedArchitectures().Select(a => a.ToString()).ToList();
        _draft.EnableDelta = EnableDelta;
        _draft.BaselineDirectory = BaselineDirectory;
        _draft.FromVersion = FromVersion;
        _draft.ExcludePatterns = ParseLines(ExcludePatternsText);
        _draft.OutputDirectory = OutputDirectory;
        _draft.SilentArgs = SilentArgs;
        _draft.InstallArgs = InstallArgs;

        _draft.NotesLanguage = NotesLanguage;
        _draft.NoteNew = ParseLines(NoteNewText);
        _draft.NoteImprovements = ParseLines(NoteImprovementsText);
        _draft.NoteFixes = ParseLines(NoteFixesText);
        _draft.NoteNotes = ParseLines(NoteNotesText);
        _draft.NoteRawMarkdown = NoteRawMarkdown;

        _draft.Status = StatusSelection;
        _draft.ForceUpdate = ForceUpdate;
        _draft.MinSupportedVersion = MinSupportedVersion;
        _draft.PublishNow = PublishNow;

        _draft.Save(_paths);
        DraftBanner = $"{Strings.T("DraftSaved")} {_draft.UpdatedAt:HH:mm:ss}";
    }

    private void RestoreDraft(WizardDraft draft)
    {
        ProjectId = draft.ProjectId ?? string.Empty;
        AppId = draft.AppId ?? string.Empty;
        ProjectKey = draft.ProjectKey ?? string.Empty;
        AppBusinessId = draft.AppBusinessId ?? string.Empty;
        PlatformSelection = draft.Platform;
        ApplyChannels(draft.Channels);

        Version = draft.Version ?? string.Empty;
        BuildNumber = draft.BuildNumber ?? string.Empty;
        SourceDirectory = draft.SourceDirectory ?? string.Empty;
        PackageTypeSelection = draft.PackageType;
        ArchX64 = draft.Architectures.Contains(nameof(UpdateArch.X64), StringComparer.OrdinalIgnoreCase);
        ArchArm64 = draft.Architectures.Contains(nameof(UpdateArch.Arm64), StringComparer.OrdinalIgnoreCase);
        ArchX86 = draft.Architectures.Contains(nameof(UpdateArch.X86), StringComparer.OrdinalIgnoreCase);
        EnableDelta = draft.EnableDelta;
        BaselineDirectory = draft.BaselineDirectory ?? string.Empty;
        FromVersion = draft.FromVersion ?? string.Empty;
        ExcludePatternsText = string.Join(Environment.NewLine, draft.ExcludePatterns);
        OutputDirectory = draft.OutputDirectory ?? string.Empty;
        SilentArgs = draft.SilentArgs ?? string.Empty;
        InstallArgs = draft.InstallArgs ?? string.Empty;

        NotesLanguage = draft.NotesLanguage;
        NoteNewText = string.Join(Environment.NewLine, draft.NoteNew);
        NoteImprovementsText = string.Join(Environment.NewLine, draft.NoteImprovements);
        NoteFixesText = string.Join(Environment.NewLine, draft.NoteFixes);
        NoteNotesText = string.Join(Environment.NewLine, draft.NoteNotes);
        NoteRawMarkdown = draft.NoteRawMarkdown ?? string.Empty;

        StatusSelection = draft.Status;
        ForceUpdate = draft.ForceUpdate;
        MinSupportedVersion = draft.MinSupportedVersion ?? string.Empty;
        PublishNow = draft.PublishNow;

        DraftBanner = $"{Strings.T("DraftRestored")} {draft.UpdatedAt:yyyy-MM-dd HH:mm}";
    }

    private void ApplyChannels(IEnumerable<string> channels)
    {
        var set = new HashSet<string>(channels, StringComparer.OrdinalIgnoreCase);
        ChannelStable = set.Contains(nameof(UpdateChannel.Stable));
        ChannelBeta = set.Contains(nameof(UpdateChannel.Beta));
        ChannelRc = set.Contains(nameof(UpdateChannel.Rc));
        ChannelPreview = set.Contains(nameof(UpdateChannel.Preview));
        ChannelDev = set.Contains(nameof(UpdateChannel.Dev));
    }
}
