# 04 客户端 SDK 与更新器设计

> 依据《AtomUpdate 产品需求》《更新器需求说明》《客户端 SDK 需求说明》整理。
> 第一部分（§1~3）为 SDK 设计，第二部分（§4~9）为更新器与端侧协议设计。

---

# 第一部分：客户端 SDK

## 1. 定位与边界

主应用引用的 NuGet 包，负责：检查更新、下载（前台/静默）、校验、展示更新日志、调度更新器、上报状态（含离线兜底补报）。

| 事项 | 负责方 |
|---|---|
| 检查更新 / 下载 / 校验 / 调度安装 / 启动更新器 / 上报 | **SDK** |
| 提示用户 | SDK 提供默认实现，可自定义 |
| 替换文件 / 运行安装器 / 提权 / 重启主程序 | 更新器 |

核心原则：引用后简单配置即可用；默认行为合理可覆盖；不强制 UI、不绑定框架；不阻塞主线程；所有网络操作可取消；所有关键步骤可观测。

## 2. 包结构与目标框架

### 2.1 NuGet 包

| 包名 | 说明 |
|---|---|
| AtomUpdate | 主 SDK，主应用引用 |
| AtomUpdate.Abstractions | 接口与模型，供扩展包引用 |
| AtomUpdate.Core | 跨端契约（DTO/枚举/常量/错误码），SDK 依赖的纯 POCO 契约包 |
| AtomUpdate.Updater | 更新器，作为内容随包分发（见 §4） |
| AtomUpdate.UI.Wpf / WinForms / Avalonia / MAUI / Console | **规划中（二阶段）** UI 适配包，当前未发布（见 §2.4/§3.12） |

> 更新器不设 Abstractions 扩展包：扩展点由更新器 hooks（进程级）+ SDK 侧可替换服务承接。

### 2.2 主包依赖

```
AtomUpdate
  ├── AtomUpdate.Abstractions
  ├── AtomUpdate.Core
  ├── Microsoft.Extensions.Configuration
  ├── Microsoft.Extensions.DependencyInjection.Abstractions
  ├── Microsoft.Extensions.Hosting.Abstractions
  ├── Microsoft.Extensions.Http
  ├── Microsoft.Extensions.Logging.Abstractions
  ├── Microsoft.Extensions.Options
  ├── Microsoft.Extensions.Options.ConfigurationExtensions
  └── 内容文件：AtomUpdate.Updater（按 RID）
```

依赖面收敛在 M.E.* 抽象与配置包，不引入具体容器/日志实现，避免与宿主冲突。

### 2.3 目标框架与应用类型

- 主目标 `net11.0`，**不支持旧版本、不做多目标**。
- 支持的应用类型：WPF / WinForms / Avalonia / MAUI 桌面 / 控制台 / **Windows 服务 / ASP.NET Core 服务**（服务型宿主无 UI：通知退化为 Console/日志，安装模式建议 onRestart）。

### 2.4 UI 适配包（可选安装）

每包提供：更新提示弹窗、下载进度窗口、更新日志展示、强制更新窗口。**UI 适配包整体为二阶段交付项，当前均未发布**；控制台形态已由 `samples/AtomUpdate.Sample.Console` 覆盖，桌面/移动宿主当前直接用 SDK 事件自行驱动 UI（见 §3.12 与 [08 §7](08-SDK接入指南.md)）。

## 3. 快速接入与公共 API

### 3.1 最简接入

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAtomUpdate(options =>
{
    options.ServerUrl = "https://update.example.com";
    options.ProjectKey = "myapp";
    options.AppId = "com.company.myapp";
    options.Channel = UpdateChannel.Stable;   // 枚举，非字符串
    options.CurrentVersion = "1.2.3";
});
```

> 完整接入示例（含 using、检查/下载/安装调用）见 [08 §2](08-SDK接入指南.md)。

### 3.2 IAtomUpdater 门面

```csharp
public interface IAtomUpdater
{
    UpdateState State { get; }
    UpdateInfo? CurrentUpdate { get; }

    Task<CheckResult> CheckAsync(UpdateTrigger trigger = UpdateTrigger.Manual, CancellationToken ct = default);
    Task<DownloadResult> DownloadAsync(DownloadMode? mode = null, CancellationToken ct = default);
    Task<CheckAndDownloadResult> CheckAndDownloadAsync(UpdateTrigger trigger, CancellationToken ct = default);
    Task<InstallResult> InstallAndRestartAsync(CancellationToken ct = default);
    void Postpone(TimeSpan duration);
    Task<CheckResult> CheckManuallyAsync(CancellationToken ct = default);
    Task<bool> ReportStartupAsync(CancellationToken ct = default);

    // 事件：CheckCompleted / UpdateAvailable / DownloadStarted / DownloadProgressChanged /
    //       DownloadCompleted / DownloadFailed / UpdateReady / InstallStarted /
    //       InstallCompleted / InstallFailed / PolicyChanged / StateChanged
}
```

典型组合：启动前台下载 → `CheckAndDownloadAsync(UpdateTrigger.Startup)`；用户点击 → `InstallAndRestartAsync()`。成员逐项说明见 [08 §4](08-SDK接入指南.md)。

### 3.3 分层子服务（均可替换，见 §3.9）

| 服务 | 生命周期 | 职责 |
|---|---|---|
| IUpdateChecker | Singleton | 检查（CheckContext：trigger/版本/通道/平台/架构/policyVersion/ETag） |
| IUpdateDownloader | Singleton | 下载（DownloadContext + IProgress\<DownloadProgress\>：进度/速度/剩余时间） |
| IUpdateInstaller | Singleton | 调度安装（InstallContext：准备更新器参数、启动更新器、主程序退出） |
| IPolicyProvider | Singleton | 策略提供与合并（`GetCurrentAsync` / `ApplyServerPolicyAsync` / `GetCachedAsync` + `PolicyChanged` 事件） |
| IUpdateNotifier | Singleton | 通知（NotificationType：UpdateAvailable/DownloadCompleted/UpdateReady/DownloadFailed/InstallFailed；支持 NotificationAction 按钮动作） |
| IUpdateReporter | Singleton | 上报（Check/Download/Install/Startup/ReportBatchAsync 五方法） |
| IStateStore | Singleton | 键值持久化抽象，默认文件实现 |
| IUpdateLogger | Singleton | 日志抽象，默认桥接 M.E.Logging |
| PollingService | HostedService | 定时轮询（可变间隔循环：抖动 ± / 失败指数退避，支持取消） |
| StartupReportService | HostedService | 宿主启动时上报活跃 + 冲刷 pending-reports 补报（与轮询开关解耦） |

### 3.4 事件模型

- 事件参数统一 `EventArgs` 子类，携带业务对象（CheckResult / UpdateInfo / DownloadProgress / PackagePath / ErrorCode+ErrorMessage / OldPolicy+NewPolicy / OldState+NewState）。
- **事件在后台线程触发**，UI 层自行调度到主线程（WPF Dispatcher / Avalonia Dispatcher.UIThread / WinForms SynchronizationContext）。
- 落地补充：下载进度经内部 `InlineProgress<T>` **同步**转发给事件，不用 BCL `Progress<T>`——后者把回调投递到捕获的上下文，既脱离后台线程，又让多次 Report 并发执行，宿主会看到百分比乱序/倒退。

### 3.5 状态机

```
Idle → Checking → NoUpdate(→Idle) | UpdateAvailable
UpdateAvailable → ForegroundDownloading | SilentDownloading → Downloaded
→ ReadyToInstall → InstallScheduled | Installing → RestartPending → Completed
任何阶段 → Failed（可重试回 Checking）；UpdateAvailable → Postponed（超期/超次 → 强制路径）
```

SDK **不消费更新器退出码**（更新器在主程序退出后独立运行，SDK 无法同进程等待其退出码）；`Installing → Completed/Failed/RolledBack` 迁移由 SDK 下次启动回读状态文件（`updater-state.json`）驱动，退出码仅供更新器自身日志/回传与人工排查（§4.6）。

安装三态语义："**标记 + 调度**"——`ReadyToInstall → InstallScheduled`（写状态文件、置 `InstallScheduled`/`RestartPending`）由 SDK 完成，`Installing → Completed/Failed` 由更新器回传驱动。解析路径：`Updater.UpdaterPath` 可解析时一律写临时配置并拉起更新器进程；解析不到时（开发场景未内嵌更新器）`OnRestart` 退化为纯调度——把包路径写入 `ScheduledInstall` 状态键并返回 `InstallResult.ScheduledOk()`（不视为失败），`Manual`/`Immediate` 则返回 `UPDATER_NOT_FOUND`。

### 3.6 触发类型与强制更新

```csharp
public enum UpdateTrigger { Startup, Polling, Manual, NetworkRecovery, Resume, Force }
```

强制更新（服务端 `forceUpdate:true` 或 current < minSupportedVersion）：前台下载、不可取消/推迟/跳过、阻止进入主界面。推迟上限与超期判定由 `AtomUpdater.Postpone` / `IsPostponeExhausted` 保证：`Postpone(duration)` 记 `PostponeCount++` 与 `PostponedUntil = now + duration`（两者持久化），超次（`>= MaxPostponeCount`）或超 `PostponedUntil` 即进入强制路径。注意 `Install.PostponeDeadline` 配置键当前**未被消费**（超期只由 `Postpone` 传入的 `duration` 决定），属保留字段。

### 3.7 配置体系（AtomUpdateOptions）

配置分 10 节：Polling / Download / Verify / Install / Notify / Cache / Updater / Report / Network / Logging。**逐键默认值与说明以 [08 §3](08-SDK接入指南.md) 为准**（接入方参考），本节只记设计要点与落地现状：

- **Download**：六触发场景 → 下载模式映射（Startup/Manual/Force=Foreground，Polling/NetworkRecovery/Resume=Silent）；下载用**无进展超时**（stall timeout，默认 60s 无字节进展才中断，替代总时长上限）。`MaxConcurrentDownloads`、`PauseOnLowBattery` 为**保留键**——前者无并发下载消费方，后者依赖未实现的电源探测（§3.11），当前均不生效。
- **Verify**：`RequireSignature`（默认 false）只校验"选中的包带 `signature`"，缺签名即 `VerifyFailed`；有签名时 SDK **跳过密码学验证并记 Warning**（公钥集分发归二阶段），真正的验签在更新器安装层（§4.13 `verification`）。该开关**仅本地配置、服务端策略不可覆盖**（否则构成降级攻击向量）。SHA256 始终强制。
- **Install**：`PostponeDeadline` 为**保留键**（未被消费，超期只由 `Postpone(duration)` 决定，见 §3.6）。
- **Logging**：`MinLevel/LogToFile/LogDirectory/RetentionDays` 均为**保留键**——SDK 日志经 `IUpdateLogger` 桥接宿主 `M.E.Logging`，无独立文件日志消费方。
- 其余节（Polling/Notify/Cache/Updater/Report/Network）键值与 08 §3 一致，此处不重复。

**配置优先级（低→高）：**

```
代码默认值 → appsettings.json → 环境变量（前缀 ATOMUPDATE_）→ 代码 AddAtomUpdate 覆盖 → 服务端下发策略（仅部分字段）
```

环境变量示例：`ATOMUPDATE_SERVERURL / ATOMUPDATE_PROJECTKEY / ATOMUPDATE_CHANNEL`。嵌套项用双下划线分段：`ATOMUPDATE_POLLING__INTERVAL=00:30:00`（等价于 `AtomUpdate:Polling:Interval`）。服务端 policy 只能覆盖策略类字段（轮询/下载/安装等，具体字段集以 03 文档 §2.3 UpdatePolicy 为准），不能覆盖身份类字段（ServerUrl/ProjectKey/AppId）与校验开关（Verify.RequireSignature）——校验开关若可被服务端关闭，即构成降级攻击向量。check 为公开端点，无需任何密钥配置。

环境变量层在 `AddAtomUpdate` 内以 `ATOMUPDATE_` 前缀剥离 + `__`→`:` 映射后叠加，位置严格排在 appsettings 之后、代码覆盖之前（`ConfigurationPrecedenceTests` 逐层断言）。`ServerUrl/ProjectKey/AppId/CurrentVersion` 为必填：`ServerUrl` 必须是 http/https 绝对地址，缺失时选项校验直接给出可读错误，而不是让 HttpClient 抛 `NullReferenceException`。

### 3.8 策略缓存与合并

- 缓存内容：policy / policyVersion / ETag / latestVersion / lastCheckedAt / **lastPayload**（上次完整检查响应，304 时据此还原结论）（`IPolicyCache`，`CachedPolicy` 模型）。
- 缓存位置：`%LocalAppData%\AtomUpdate\cache\{appId}\`（macOS `~/Library/Application Support/AtomUpdate/cache/{appId}/`，Linux `~/.config/AtomUpdate/cache/{appId}/`）；按应用隔离，避免同机多应用互相覆盖策略缓存。
- **策略合并（服务端下发字段可能不完整）**：`SDK 内置默认 → 本地配置 → 服务端上次下发 → 服务端本次下发`。
- `policyUnchanged`（服务端省略 policy 字段）时，缓存必须**保留上一次的服务端策略与 policyVersion**，不能被 null/0 覆盖——否则下次检查退化为内置默认，表现为"同一台机器第二次手动检查不再自动下载"（有 E2E 回归与单测守护：`UpdateCheckerTests.PolicyUnchanged_KeepsCachedServerPolicy`）。

### 3.9 依赖注入与扩展点

```csharp
services.AddAtomUpdate(options => { ... })
        .AddNotifier<MyNotifier>()      // 替换 IUpdateNotifier
        .AddReporter<MyReporter>()      // 替换 IUpdateReporter
        .UseCustomUI<MyUpdateUI>();     // 替换 IUpdateUI
```

可替换服务全集：IUpdateChecker / IUpdateDownloader / IUpdateInstaller / IPolicyProvider / IUpdateNotifier / IUpdateReporter / IStateStore / IUpdateLogger / IPlatformInfo / INetworkStatus / IPowerStatus / IUpdateUI。HttpClient 由 IHttpClientFactory 管理（命名客户端 `AtomUpdate`），Polly 重试可选。

落地补充：**不提供 `AddNotifier/AddReporter/UseCustomUI` 流式扩展**——`AddAtomUpdate` 内部一律用 `TryAddSingleton` 注册分层实现，宿主在调用 `AddAtomUpdate` 之前（或之后自行 `Replace`）注册同名服务即可覆盖，效果与流式扩展等价且少一层 API 面。示例：先 `services.AddSingleton<IUpdateNotifier, MyNotifier>()` 再 `AddAtomUpdate()`。INetworkStatus/IPowerStatus/IUpdateUI 随 §3.11/§3.12 的适配包一并提供，当前未实现。**未引入 Polly**：检查/上报/退避重试由 SDK 内部实现（保持零第三方依赖与 AOT/裁剪面收敛），`IHttpClientFactory` 仅承担命名客户端生命周期。

### 3.10 HTTP 客户端契约

- 请求头：`User-Agent: AtomUpdate/0.1.0 ({os}; {arch})`（版本段为固定串，非程序集版本）、`If-None-Match: {etag}`（公开端点无需鉴权头；管理端 JWT 用 `Authorization`）。
- 超时：检查 30s / 上报 15s / 下载采用**无进展超时**（stall timeout，60s 无字节进展才中断，无总时长上限）。
- 重试：检查 3 次指数退避；上报失败即落 `pending-reports`（上报当前为单次 POST、无在线重试，可靠性由启动补报承担）；下载 3 次 + 断点续传。
- 代理：NetworkOptions.Proxy 或系统默认代理；所有调用支持 CancellationToken。

落地补充：

- 命名客户端三个：`AtomUpdate`（检查）、`AtomUpdate.Download`（下载）、`AtomUpdate.Report`（上报）。检查与上报都以 `ServerUrl` 为 `BaseAddress`（路由常量是相对路径，漏配 BaseAddress 会抛 `InvalidOperationException`，E2E 回归）；下载客户端不设 BaseAddress——包地址由服务端下发，必须是绝对 URL。
- 无进展判定不依赖响应流的取消：读操作可能长时间不响应 `CancellationToken`（代理停顿、非常规内容），因此用独立的看门狗任务与读任务竞速，超时即判失败。
- 下载重试耗尽后保留**末次失败的错误码与原因**（而不是笼统 NETWORK_ERROR），宿主可按 VERIFY_FAILED/DOWNLOAD_FAILED 分支。
- 上报失败包含两类：请求异常与**非 2xx 响应**（例如批量端点尚未上线返回 404），两者都按 `FallbackToLocal` 落 `pending-reports` 等补报。
- `HttpRequestMessage` 不可重复发送：检查/下载的重试循环每轮都新建请求对象。
- **平台错配的表现**：客户端配置的 `AppId` 若与所属应用注册的平台不一致，服务端 `/api/check` 返回 400 `PLATFORM_MISMATCH`；SDK 当前按通用非 2xx 处理，表现为 `SERVER_ERROR` + "检查更新失败：HTTP 400"（服务端 message 里点名了 AppId 与两个平台，但 **SDK 未透传响应体**）。这是刻意保留的"配置错误要响"——它不会退化成"已是最新版本"的假象。把服务端错误码/文案透传进 `OperationResult`（宿主可直接显示"平台配错了"）归后续改进。

### 3.11 平台适配接口

| 接口 | 内容 | 平台实现 |
|---|---|---|
| IPlatformInfo | Platform/Arch/OsVersion/Runtime/IsMeteredNetwork/IsOnBattery/BatteryLevel | Windows：WMI/Registry；macOS：sysctl/defaults；Linux：/proc、/etc |
| INetworkStatus | IsConnected/IsMetered + NetworkChanged 事件 | NLM/SCNetworkReachability/NetworkManager |
| IPowerStatus | IsOnBattery/BatteryLevel | PowerManager/IOKit/upower |

平台探测现状：`IPlatformInfo` 实现 Platform/Arch/OsVersion/Runtime；`IsMeteredNetwork/IsOnBattery/BatteryLevel` 与 `INetworkStatus/IPowerStatus`（含 NetworkChanged 事件）未实现，因此**计量网络限制、低电量暂停、网络恢复/休眠恢复触发的补检**当前均未生效：`AllowMeteredNetwork`/`PauseOnLowBattery`/`DownloadOnNetworkRecovery`/`DownloadOnResume` 只作为配置与策略合并的字段存在，下载器与轮询都没有查询网络/电源状态的消费方（触发源也缺失），落地归后续迭代。

### 3.12 UI 集成

- SDK 不强制 UI，提供事件与 API；默认 UI 适配通过可选包 `AtomUpdate.UI.*`。
- `IUpdateUI`：ShowUpdateAvailableAsync / ShowDownloadProgressAsync / ShowReadyToInstallAsync / ShowForceUpdateAsync。
- 控制台模式：输出到控制台、不阻塞、适合服务端宿主。
- WPF / WinForms / Avalonia / MAUI：UI 回调经 Dispatcher/SynchronizationContext 调度。

SDK 只保证事件在后台线程同步触发；宿主自行调度到 UI 线程。`AtomUpdate.UI.*` 适配包为二阶段交付项，控制台形态已由 `samples/AtomUpdate.Sample.Console` 覆盖（Generic Host；同一形态改 Windows 服务只需额外引用 `Microsoft.Extensions.Hosting.WindowsServices` 并 `builder.Services.UseWindowsService()`，通知在无 UI 宿主下本就是 Console）。

### 3.13 通知适配

NotifyMode：Auto / System / Tray / InApp / Console / None。Auto 选择逻辑：有 UI 框架 → InApp；无 UI 框架 → System Toast；控制台 → Console。MVP 至少落地 Auto + Console + Windows Toast，Tray/InApp 二阶段；**MVP 中 InApp 未实现时 Auto 自动回落 System Toast/Console**。

当前内置只有 `ConsoleNotifier` 一个实现（`Auto/System/Tray/InApp` 一律回落 Console，`None` 静默）；Windows Toast 未接入——通知是纯呈现层、可用 `IUpdateNotifier` 覆盖。通知文案取自 `Notify.TitleTemplate/BodyTemplate`（`{version}` 占位），`NotificationContext.Type` 携带类型供自定义实现分支；因此同一模板下"发现新版本"会在检查命中与下载就绪各提示一次（受 `ShowOnReady` 等开关控制）。

### 3.14 持久化布局

| 数据 | 位置（相对 AtomUpdate 根目录） |
|---|---|
| installationId | state/{appId}/installation.json（匿名 UUID，首次生成，不重置；按应用隔离——Installation 按 App 注册，共享会撞主键） |
| machineFingerprint | state/{appId}/fingerprint.json（硬件哈希含**应用级盐** `SHA256(machineId|appId)` 取前 32 hex，按应用隔离；可由 Report.ReportMachineFingerprint=false 关闭） |
| policy / 上次检查时间 | cache/{appId}/policy.json（上次检查时间作为其 `LastCheckedAt` 字段，**不单独成 last-check.json**；按应用隔离） |
| **待上报事件** | state/{appId}/pending-reports/（更新器失败落盘 + SDK 上报失败落盘；启动时读取补报，成功后删除。按应用隔离） |
| 下载缓存 | cache/{appId}/downloads/（按应用隔离） |
| **更新器回传状态** | state/{appId}/updater-state.json（更新器写、SDK 启动回读补报，见 §5.3） |
| **更新器可执行** | updater/{version}/ + updater/current 指针文件（见 §5.2） |
| 更新器日志 | logs/updater/updater-{yyyyMMdd}.log（04 §4.14，跨应用共用目录，文件名不含 appId） |

**跨组件契约路径不可覆盖**：`updater-state.json` 与 `updater/` 目录固定在平台 AtomUpdate 根（`%LocalAppData%\AtomUpdate` 等），**不受 `Cache.CacheDirectory` 影响**——更新器是独立进程，只认 §4.4/§5.2 的文档化位置；SDK 回读时同样走 `PlatformPaths.ResolveStateRoot()`，两侧路径由 `ConsistencyTests` 与单测守护。`FileStateStore` 自身的 `stateRootOverride` 仅作用于 SDK 私有状态（installation/policy/pending-reports），不能拿它对齐更新器路径。

落地补充：

- **补报触发方**是 SDK 内置的 `StartupReportService`（IHostedService），宿主启动即冲刷队列，与 `Polling.Enabled` 无关——挂在轮询服务里会让关闭轮询的应用队列只增不减。`IAtomUpdater.ReportStartupAsync` 同时对外暴露，宿主可手动补触发一次。
- 补报顺序：`ReportStartupAsync` 先回读 `updater-state.json` 补报更新器结果并回写 `reported=true`，**再**冲刷 pending-reports 队列。更新器回传失败时不会往队列写文件（它只写状态文件），因此两条路径必须有序，否则同一事件双投；闸门只取 `Report.Enabled`，不受 `Report.ReportOnStartup` 影响（后者只管 SDK 自身启动事件）。
- 队列一条事件一个文件（`state/{appId}/pending-reports/{utc}-{guid}.json`），补报按文件名序打包成一个 batch；成功即删除，失败继续留在队列。
- `last-check.json` 未单独成文件：上次检查时间作为 `policy.json` 的 `LastCheckedAt` 字段存储（等价信息，少一次 IO 与一处失效面）。
- 落盘根目录可由 `FileStateStore`/`UpdateReporter` 的 `stateRootOverride` 参数改写（仅供测试隔离，单测因此不污染真实 `%LOCALAPPDATA%`；生产不暴露该配置，位置仍由平台决定）。

### 3.15 错误处理

- 异常层级：`AtomUpdateException` → UpdateCheckException / UpdateDownloadException / UpdateInstallException / UpdateVerifyException。
- 错误码（字符串）：CHECK_FAILED / NETWORK_ERROR / SERVER_ERROR / INVALID_RESPONSE / DOWNLOAD_FAILED / VERIFY_FAILED / INSTALL_FAILED / ELEVATION_REJECTED / UPDATER_NOT_FOUND / STATE_INVALID / UNKNOWN。
- 结果模型 `OperationResult<T>`（Success/Data/ErrorCode/ErrorMessage/Exception）；公开 API 倾向返回结果对象而非抛异常，异常保留给编程错误。

### 3.16 线程模型

- 不阻塞主线程；所有异步操作返回 Task；轮询注册为 IHostedService（`PollingService` 以可变间隔循环实现——抖动/退避需要非固定周期，未用 `PeriodicTimer`）；下载用 HttpClient 流式 + IProgress\<T\>；事件在后台线程触发。
- 落地补充：`IProgress<T>` 由内部 `InlineProgress<T>` 同步实现（见 §3.4），下载循环节流为"每 64KB 或每 250ms 一次"进度回调；`PollingService` 与 `StartupReportService` 各为一个 BackgroundService，`IAtomUpdater` 内部用 `SemaphoreSlim` 串行化**检查与下载**（`CheckAsync`/`DownloadAsync`/`CheckAndDownloadAsync` 共用一把闸），避免轮询与宿主手动调用交错；`InstallAndRestartAsync` **不入闸**（安装是终态动作，调度后立即返回或拉起更新器，无需与检查/下载互斥）。

### 3.17 SDK 内部目录结构

```
AtomUpdate（程序集）
  ├── DependencyInjection / Configuration
  ├── Check / Download / Install / Policy / Notify / Report
  ├── State / Platform / Polling / Logging / Internal
AtomUpdate.Abstractions
  ├── Enums / Events / Exceptions / Interfaces / Models
```

---

# 第二部分：更新器与端侧协议

## 4. 更新器（AtomUpdate.Updater）

### 4.1 定位与核心原则

独立可执行进程，主程序退出后执行安装。核心原则：不引用主程序，只接收参数和配置；不依赖主程序运行环境；不假设网络可用；所有关键操作可回滚；所有关键步骤写日志和状态文件；不需要提权则不提权。

### 4.2 进程模型

```
MyApp.exe（普通权限）
   ↓ UseShellExecute=false, CreateNoWindow 启动
AtomUpdate.Updater.exe（普通权限启动，按需提权）
   ├─ 需要提权 → 重新以提权方式启动自己
   ├─ 需要自升级 → AtomUpdate.Updater.New.exe --replace-old <旧路径> --replace-pid <PID> --config <handoff>
   └─ 安装完成 → 启动 MyApp.exe --skip-update-check
```

### 4.3 命令行参数

```
AtomUpdate.Updater.exe
  --config <path>  --package <path>  --target <path>
  --from-version <v>  --to-version <v>  --package-type <zip|exe|msi|dmg|pkg|appimage|deb|rpm>
  --install-mode <manual|onRestart|immediate>  --install-args <args>
  --sha256 <hash>  --signature <sig>  --key-id <id>
  --restart <path>  --restart-args <args>
  --server-url <url>  --installation-id <id>  --machine-fingerprint <fp>
  --app-id <id>  --project-key <key>
  --replace-old <path>  --replace-pid <pid>
  --log-level <level>  --log-path <dir>
  --dry-run  --no-restart  --no-rollback  --verbose  --help
```

- 优先级：命令行 > 配置文件 > 内置默认值（完整五层见 §4.13）；必填 `--package / --target / --to-version / --package-type`；校验失败退出码 `0x1002`。逐项说明见 [10 §3](10-更新器配置参考.md)。

### 4.4 状态文件

路径 `{stateRoot}/{appId}/updater-state.json`（按 appId 隔离，避免同机多应用互相覆盖回传状态）；平台目录与**完整字段表见 [10 §5/§6](10-更新器配置参考.md)**。此处只记设计要点：

- 关键字段：`stage`（starting/verifying/installing/completed/failed/rolledBack）、`result`、`errorCode`（`0x1004` 这类十六进制字符串）、`reported`、`updaterVersion`（取更新器程序集 InformationalVersion，随回传上报供服务端定位"哪个更新器版本出的问题"）；补报字段与 §5.3 回传内容对齐。
- `retryCount` 由同 from→to 的上一份状态递增（指同一目标版本的重试安装次数，非网络重试）；回滚失败时 errorCode/errorMessage 改写为 `0x1009` 并保留原始错误文本；`reported` 写入时机见 §5.3。字段集与 SDK 侧 `UpdaterStateFile` 的镜像关系由 `ConsistencyTests` 反射守护。
- 生命周期：启动写 `starting` → 安装写 `installing` → 终态 → 尝试回传（失败保持 `reported=false` 等 SDK 补报，见 §3.14 pending-reports）。

### 4.5 主流程

```
[启动]
 → 解析参数 → 加载配置 → 初始化日志
 → 获取全局互斥（失败 → 0x1001）
 → 自升级检查（需要 → New.exe --replace-old → 退出）
 → 读取既有状态文件
 → 校验参数（失败 → 0x1002）
 → 校验更新包 SHA256 / 签名（失败 → 0x1004；提供 `--sha256` 即强制校验、未提供则跳过，签名按 verification.requireSignature、默认不验）
 → 提权预判（需要且未提权 → 自我提权；被拒 → 0x1003）
 → preInstall → 备份 → preReplace → 替换/运行安装器
 → postReplace → postInstall
 → 写成功状态 → 回传服务器
 → 重启主程序 → 清理临时文件 → 释放互斥 → 退出（0x0000）
```

实现顺序与上图按"强度优先"调整（`UpdaterApp.RunAsync` 顶部注释同步留档）：

- **参数/包校验在自升级交接之前**：先 `TaskValidation` + `PackageVerifier`（SHA256/签名）再决定是否交接，避免用未校验的任务去拉新更新器（0x1002/0x1004 先于交接返回）。
- **dry-run 在校验之后立即退出**：只跑参数+包校验，不写状态文件、不交接、不提权、不回传、不重启（退出 0x0000）——上图把 dry-run 当作贯穿标志，实现里它是一个提前返回的门。
- **自替换（`--replace-old`）在互斥之前**：交接给 `Updater.New.exe` 的替换端跑在旧进程仍持有互斥时，若排在互斥获取之后会自锁死。
- **钩子顺序**：preInstall → preReplace →（备份 + 替换/安装器）→ postReplace → postInstall；与上图"preInstall → 备份 → preReplace"语义等价（备份动作在 zip 替换链路内部完成，`install.backupBeforeReplace=false` 时跳过）。替换之后失败的 postReplace/postInstall 钩子携带回滚清单，preInstall/preReplace 阶段失败没有可回滚清单。
- **写状态文件的时点**：互斥与参数校验通过（且非 dry-run）后写 `starting` → 置 `verifying` 做包校验 → 交接/提权后置 `installing` → 终态 `completed/failed/rolledBack`；`retryCount` 由上一次同 from→to 的状态递增。
- **重启失败不回退安装结果**（0x100A 仅记状态/日志）；`restart.enabled=false` 时跳过 preRestart/postRestart 钩子。
- **清理**：成功后按 `rollback.keepBackupOnSuccess` 删除或保留本次备份（保留时按 `install.backupRetentionDays` 顺带清理过期备份）。

### 4.6 退出码体系

退出码 `0x0000`（成功）/ `0x1001`~`0x100B`（互斥/参数/提权/校验/备份/替换/安装器/钩子/回滚/重启/未知失败），**完整表见 [10 §4](10-更新器配置参考.md)**；常量定义在更新器 `ExitCodes`，与 SDK 侧由 `ConsistencyTests` 守护一致。

退出码写入状态文件 `errorCode` 并随回传上报，供服务端定位与人工排查；**SDK 当前不同进程消费退出码驱动 UI**（更新器在主程序退出后独立运行，见 §3.5）。"按 `0x1003` 弹授权界面循环、`0x1001` 稍后重试"等 UI 联动为规划项，尚未落地。

### 4.7 各包类型安装

| 类型 | 流程要点 |
|---|---|
| zip | 校验 → 解压临时目录 → 校验内容 → 备份 → 替换策略 → deleteOrphans（可选）→ 清理 |
| zip（delta） | 同 zip 流程，另：替换完成后执行包内 `delete.json` 删除清单（尊重 exclude/preserve），manifest.json 校验 fromVersion 与当前版本一致 |
| exe / msi | 校验 → 备份（可选）→ silentArgs / `msiexec /i /quiet` → 等待退出码 → 校验结果 |
| dmg / pkg | hdiutil attach 复制 detach / `installer -pkg` |
| AppImage / deb / rpm | 替换 + chmod +x / dpkg -i / rpm -ivh（失败提示用户） |

MVP：zip + exe（Inno Setup）；msi 二阶段；dmg/pkg/AppImage/deb/rpm 第三阶段（IInstaller 接口一次到位）。

实现要点：

- 已实现 `ZipInstaller`（zip 全包 + zip delta）与 `ExeInstaller`；其余类型统一走 `UnsupportedInstaller` → **明确失败 0x1007**（记日志"该包类型的安装器尚未实现"），不静默跳过。接口名为 `IPackageInstaller` + `PackageInstallerFactory`（按 `packageType` 分派）。
- delta 识别：`kind` 缺省时按包内容自动判定（包内同时含 `manifest.json` 与 `delete.json` 即 delta）；SDK 侧只在资产 `kind=delta` 时显式写 `"delta"`，全包留 null（见 §5.1）。manifest 的 `fromVersion` 与任务 `fromVersion` 不符 → 0x1004。
- `ExeInstaller` 不产生回滚清单（文件替换由安装器自行管理），因此 exe 路径失败后不回滚；参数拼接为 `silentArgs`（始终取包元数据）+ `installArgs`，其中 `installArgs` 优先级：任务顶层 `installArgs`（SDK 写入包元数据值；`--install-args` 若给出则**覆盖**它）→ 回退 `install.installArgs` 配置节。
- 替换引擎在 `Replace/`：`FileReplacer`（占用重试 `install.replaceRetryCount`/`replaceRetryDelayMs`，超限 0x1006）+ `GlobMatcher` + `BackupManifest` + `RollbackManager`；备份由 zip 链路内部完成（`Backup/` 不单独成模块）。

### 4.8 钩子系统

- 阶段（6 个，`HookStages` 常量）：preInstall / postInstall / preReplace / postReplace / preRestart / postRestart。**无 preDownload/postDownload**（下载在 SDK 侧，更新器不下载业务包）。
- 执行：Windows `.bat/.cmd`→`cmd /c`、`.ps1`→`powershell -ExecutionPolicy Bypass -File`、`.exe` 直接执行；macOS/Linux `.sh`→`/bin/sh`，可执行文件直接执行。
- 规则：超时 `hookTimeoutSeconds`（默认 60）视为失败；`hookFailPolicy`：abort（中断+回滚）/ continue；输出写日志；工作目录为目标目录。
- 环境变量注入：`ATOMUPDATE_FROM_VERSION / TO_VERSION / TARGET / PACKAGE / STAGE / LOG_PATH / LOG_DIR / INSTALLATION_ID`。

### 4.9 替换策略

- merge（只覆盖包内文件）/ replace（整目录替换）；exclude / preserve / deleteOrphans / backupBeforeReplace。
- glob：精确、单层（`logs/*`）、递归（`data/**`）、任意层级（`**/*.log`）；Linux 大小写敏感。
- 执行顺序：备份 → 扫描包文件 → 逐文件（exclude 跳过否则替换）→ deleteOrphans（preserve 保留，其余删除）→ 清理。
- 占用冲突：重试 N 次 → 失败 0x1006；Windows rename-replace 缓解。

### 4.10 提权

- 预判（`onDemand` + `skipIfNotNeeded`）：向目标目录写探测文件（`.atomupdate-write-test-{guid}`）——可写则不提权，`UnauthorizedAccessException` 则需要提权；**仅写探测，无按路径（Program Files / /Applications 等）预判**。已提权（`IsElevated`）直接跳过，避免重复弹 UAC。
- 方式：Windows `runas`；macOS `osascript`；Linux `pkexec/sudo`。
- 拒绝闭环：状态文件 `elevationRejected=true, reason=access_denied` → 回传 → `0x1003` → 主程序授权界面（无次数限制，允许退出，不授权不能继续使用）→ 重新授权后 SDK 以 runas 重启。
- 配置：elevation.mode（onDemand/always/never）、retry（`unlimited` 或数字，**本进程内 `unlimited` 亦按单次尝试处理**，跨次授权循环由 SDK 负责）、allowExit、skipIfNotNeeded。

### 4.11 全局互斥

| 平台 | 机制 |
|---|---|
| Windows | `Global\AtomUpdate.Updater.{appId}` 命名互斥体 |
| macOS / Linux | `/tmp/atomupdate.updater.{appId}.lock` 文件锁（记录 PID，启动时校验进程存在） |

获取失败直接退出 `0x1001`（不提示用户，只写日志）；`mutexTimeout` 默认 0 不等待。互斥按 appId 隔离，同机多应用可并行更新、同一应用不会并发安装。

### 4.12 更新器自身升级（双进程）

```
Updater.exe → 自升级检查（checkBeforeInstall；失败/断网时记日志跳过，继续原任务）
  ├─ 无新版本 → 正常执行
  └─ 有 → 下载 Updater.New.exe → 启动 New.exe --replace-old（原任务参数经临时文件传递）→ 退出
       → New.exe：等待旧进程退出 → 备份 Updater.exe.bak → 覆盖 → 校验 → 删备份 → 继续原任务
```

替换失败恢复 `.bak`；恢复失败记录日志继续用旧版本。推荐：自升级由更新器自己处理，SDK 不感知。

实现要点：

- **触发条件**：`selfUpdate.enabled && checkBeforeInstall` 且配置了 `selfUpdate.manifestUrl`；**未配置 manifestUrl 直接跳过**（Debug 日志）。清单为内部约定 `GET → {version,url,sha256}`，独立分发契约归二阶段。
- **任何异常都降级为"跳过自升级、继续原任务"**（清单拉取失败、SHA256 不符、下载失败、新进程启动失败）——不阻断安装；只有成功交接才让本进程以 0x0000 退出。
- **交接方式**：新二进制下载到 `%Temp%\atomupdate\selfupdate\AtomUpdate.Updater.New.exe`（非 Windows 置 755），任务经**全量已解析配置**序列化到同目录 `handoff-{guid}.json` 传递（`UpdaterTaskFile` 与临时配置同构，见 §5.1），启动 `New.exe --replace-old "{旧路径}" --replace-pid {PID} --config "{handoff}"`。
- **替换端顺序**：等旧进程退出（上限 30s）→ 旧文件复制为 `.bak` → 新文件先落同目录 staging 再 `File.Move(overwrite)` 覆盖 → 校验 → 删备份；失败从 `.bak` 恢复，恢复失败记日志继续用旧版本。替换后由新进程用同一 handoff 任务继续原安装，不重复执行。
- 版本比较忽略 `+build`/`-prerelease` 后缀（MVP 口径，`SelfUpdater.CompareVersions`）；远端版本 ≤ 当前版本视为无需升级。
- `SelfUpdater.CurrentVersion` 取更新器程序集的 `AssemblyInformationalVersion`，写入状态文件 `updaterVersion` 并随回传上报（§5.3）。

### 4.13 更新器配置体系

优先级（低→高）：内置默认 → 安装目录 `updater.default.json` → `--config` 指定文件 → 环境变量 `ATOMUPDATE_UPDATER_CONFIG` → 命令行参数。其中 `verification` 节仅取前两层（内置默认 → `updater.default.json`），不参与运行时参数合并——防同机攻击者注入降级校验。

配置分 9 节：updater / verification / selfUpdate / elevation / install / rollback / restart / report / network。**逐键默认值与说明以 [10 §2](10-更新器配置参考.md) 为准**（查阅型参考），本节只记设计要点：

- `verification`（requireSignature / publicKeys）只信任前两层，运行期传入一律丢弃（见下 `SettingsLoader` 要点）；publicKeys 随客户端分发内嵌，不从 Api 动态获取。
- `selfUpdate.mode` / `selfUpdate.serverUrl` 为**保留键**（无消费方）；自升级实际由 `enabled` + `checkBeforeInstall` + `manifestUrl` 驱动（§4.12）。
- `elevation.retry` 的 `unlimited` 按单次尝试处理（§4.10）；`report.fallbackToLocal` 在更新器侧为**保留键**（回传失败一律写 `reported=false`，§5.3）。
- `restart.waitForExitMs`（毫秒）可被任务字段 `waitForMainProcessExitSeconds`（秒）覆盖（§5.1/§5.2）。

实现要点（`UpdaterSettings` + `TaskValidation`）：

- JSON 键 = 属性名 camelCase（`mutexTimeoutSeconds` / `replaceRetryCount` …），与 [10 §2](10-更新器配置参考.md) 各表一致；非法值在 `TaskValidation` 阶段报参数错误 0x1002（枚举串大小写不敏感：`onDemand`/`ondemand` 均可）。
- **`report.enabled=false` 时状态文件直接写 `reported=true`**：本次安装不该由 SDK 补报（§5.3 回读据此跳过），不是"回传失败"。
- `elevation.retry` 是字符串（`unlimited` 或数字），且只约束**本进程内**的连续提权（`unlimited` 亦按单次尝试处理）；跨次授权循环由 SDK 负责（§4.10）。
- `install.hooks` 值可为单个命令字符串或命令数组（顺序执行），由 `HookCommandListConverter` 手写解析（AOT 无反射）。
- `verification` 节只信任两层的语义落实在 `SettingsLoader`：合并完运行层（`--config` + 环境变量文件）后，把 JSON 的 `verification` 节点整体替换回"内置默认 + `updater.default.json`"的结果，因此任何运行期传入的 `verification.*` 都被丢弃。同理 `updater.default.json` 缺失是允许的，而 `--config`/环境变量指向的文件缺失直接算参数错误 0x1002。
- `network.*` 作用于自升级清单/下载与回传客户端；`report.timeoutSeconds`/`retryCount` 只作用于回传（3 次指数退避）。
- 命令行只覆盖显式给出的项（`ApplyCli` 逐字段 `is not null` 判断）：`--no-restart`→`restart.enabled=false`、`--no-rollback`→`rollback.enabled=false`、`--dry-run`→`updater.dryRun=true`、`--verbose`→`updater.logLevel=debug`；`installMode` 全链路缺省为 `onrestart`。

### 4.14 日志与回滚

- 日志：按天切分、保留 7 天（可配）；Windows `%LocalAppData%\AtomUpdate\logs\updater\`，macOS `~/Library/Logs/AtomUpdate/updater/`，Linux `~/.local/share/AtomUpdate/logs/updater/`；不记录私钥/密码。
- 回滚触发：替换失败 / 安装器失败 / 钩子失败且 abort / 未预期异常（且 `rollback.enabled && onFailure` 并持有回滚清单）。流程（`FailAsync`）：`RollbackManager.Restore` 从备份恢复 → 写终态状态（`rolledBack`；恢复失败则 `failed` + `0x1009`）→ 回传。**无恢复后校验、无重启旧版本、无用户中断触发**（更新器在主程序退出后无头运行）。回滚也失败（0x1009）→ 状态记原始错误 + 备份目录，提示手动恢复。
- 备份保留：成功默认删除（keepBackupOnSuccess=false），保留 3 天。

### 4.15 更新器模块划分

```
AtomUpdate.Updater
  ├── Cli / Configuration（SettingsLoader·TaskValidation·UpdaterSettings·UpdaterTaskFile）/ State / Locking / Logging / Json
  ├── Verification（PackageVerifier）
  ├── Installers（IPackageInstaller + Zip / Exe / Unsupported + PackageInstallerFactory）
  ├── Replace（FileReplacer / GlobMatcher / BackupManifest / RollbackManager，含备份）
  ├── Hooks / Elevation / SelfUpdate / Report / Restart
  └── Program / UpdaterApp / ExitCodes / UpdaterPaths / UpdaterFailureException
```

实现要点：目录即上表（`Locking/GlobalMutex.cs`）；备份与回滚在 `Replace/` 内（无独立 `Backup/` 模块）；`IInstaller` 实名 `IPackageInstaller`；未支持包类型由 `UnsupportedInstaller` 明确返回 0x1007（§4.7）。

更新器不提供自定义 Pipeline：IUpdatePipeline + AssemblyLoadContext 插件加载与 Native AOT 互斥，且扩展点由 hooks 进程级钩子与 SDK 侧可替换服务基本覆盖；若未来出现"替换替换/回滚引擎本身"的需求，再评估非 AOT 构建方案。更新器不引用 AtomUpdate.Core：report 回传模型由更新器内部定义（JSON source-gen 序列化，AOT 约束），临时配置与状态文件为文档化格式。

## 5. SDK ↔ 更新器交互协议

### 5.1 临时配置文件

SDK 启动更新器前写入 `%Temp%\atomupdate\updater-{guid}.json`（macOS/Linux `/tmp/atomupdate/`）：

```json
{
  "packagePath": "...", "targetPath": "...", "fromVersion": "1.2.3", "toVersion": "1.3.0",
  "packageType": "zip", "kind": "delta", "installMode": "onrestart",
  "sha256": "...", "signature": "...", "keyId": "...",
  "restartPath": "...", "restartArgs": "--skip-update-check",
  "serverUrl": "...", "installationId": "...", "machineFingerprint": "...",
  "appId": "...", "projectKey": "...", "platform": "windows", "arch": "x64",
  "silentArgs": "/SILENT", "installArgs": "...", "waitForMainProcessExitSeconds": 5
}
```

实现要点：

- **字段与更新器 `UpdaterTaskFile` 顶层运行字段一一对应**（camelCase，`AtomUpdateJson` 同款配置：枚举转字符串 + null 省略）。由 `UpdateInstaller.CreateLaunchConfig(context, options, platformInfo, installationId, machineFingerprint)` 纯函数构造，`ConsistencyTests` 用反射校验"SDK 侧新增/改名字段必定在更新器侧存在同名属性"，并做一次真实 round-trip 逐字段比对——防止 SDK 加了字段而更新器静默忽略。
- 取值口径：`targetPath` = `restartPath` 的目录（即应用安装目录）；`packageType`/`kind` 取自选中资产，**全量包不写 `kind`**（留 null 让更新器按包内容自动识别，§4.7）；`installMode` 小写（`onrestart`/`immediate`/`manual`）；`platform` 来自 `IPlatformInfo`、**`arch` 取自选中包资产（见下条）**（供签名内容校验，05 §8）；`silentArgs`/`installArgs` 来自包资产元数据；`restartArgs` = `Install.RestartArgs`（默认 `--skip-update-check`）；`waitForMainProcessExitSeconds` = `Updater.WaitForMainProcessExit`（默认 5，覆盖更新器 `restart.waitForExitMs`）。
- 更新器实例不写配置节（`updater`/`install`/`report` 等），节值仍由更新器自己的五层合并得出；只有自升级交接文件例外——它序列化**已解析的全量** `UpdaterTaskFile`（§4.12）。
- 安全约束：包未签名时省略 `signature`/`keyId`；校验开关不经该文件传递（`verification` 节锁定，§4.13）。**清理已落地**：更新器在 `RunAsync` 的 `finally` 删除本组件写入的启动配置（仅 `%Temp%\atomupdate\updater-*.json` 与 `selfupdate\handoff-*.json`，用户手写的 `--config` 保留；提权回环时子进程先删、父进程静默跳过），失败/异常同样清理。未显式设 ACL，依赖 `%Temp%` 目录的默认用户级权限；更新器被强杀时可能残留临时配置文件（含安装任务信息），属已知残余风险。
- **`platform`/`arch` 的验签口径**：更新器按 `sha256|toVersion|platform|arch` 构造签名内容；`/api/check` 的 `PackageAsset` 必填回传**资产架构**，SDK `UpdateInstaller.CreateLaunchConfig` 的 `arch` **取选中包 `package.Arch`，仅当包未带架构时才回落 `IPlatformInfo.Arch`（设备架构）**。arm64 机器回落到 x64 包时验签内容即用 `arch=x64`，与发布方按资产 arch 的签名口径一致（签名内容规范见 05 §8；更新器验签按元数据有无签名自动跳过/启用）。详见 03 §3.2。

### 5.2 启动与退出时序

```
SDK: Process.Start(Updater.exe, --config <临时配置>)  UseShellExecute=false, CreateNoWindow=true
SDK: 主程序立即退出
Updater: 等待主程序退出（WaitForMainProcessExit=5s，超时强制继续）
Updater: 安装 → delayMs(500) 后重启主程序（带 --skip-update-check）
```

`AutoExtractUpdater=true` 时 SDK 首次运行先把内嵌更新器释放到版本化目录 `%LocalAppData%\AtomUpdate\updater\{version}\`（macOS/Linux 对应路径），`Updater.UpdaterPath` 可指向自定义位置。

实现要点（`Install/UpdaterProvisioner.cs`）：

- **更新器路径解析顺序**：`Updater.UpdaterPath`（配置了但文件不存在 → 记 Warning 后返回 null，不静默回落）→ `AutoExtractUpdater=false` 时不释放 → 否则 `UpdaterProvisioner.EnsureProvisioned(platformInfo, logger)`。拿不到更新器时：`onRestart` 模式落调度状态（`install.scheduled`）并 Warning（纯调度降级，源码引用/无内嵌产物的开发场景）；`immediate`/`manual` 返回 `UPDATER_NOT_FOUND`。
- **包内布局 → 释放位置**：NuGet 原生资产约定 `runtimes/{rid}/native/AtomUpdate.Updater[.exe]`，释放到 `updater/{sdkVersion}/AtomUpdate.Updater[.exe]`，并把版本号写入 `updater/current` **指针文件**（内容 = 版本字符串）。`{sdkVersion}` 取 SDK 程序集版本三段（`ToString(3)`，如 `0.1.0`）；版本化目录避免覆盖正在运行的更新器实例。
- **幂等**：`current` == 目标版本 && 目标文件存在 && 长度与内嵌源一致 → 跳过复制（只重写指针）；否则先写 `{dest}.tmp` 再 `File.Move(overwrite: true)` 原子落位，非 Windows 追加 `UserExecute` 位（继承源权限）。失败记 Error 返回 null。
- **RID 来自平台枚举，不用 `RuntimeInformation.RuntimeIdentifier`**：`UpdatePlatform`×`UpdateArch` → `win|osx|linux` + `x64|arm64|x86`（平台归 App、架构归包，SDK 侧由 `IPlatformInfo` 探测）。原因：`RuntimeIdentifier` 可能是 `linux-musl-x64` 这类包内不存在的 RID。探测顺序：`runtimes/{rid}/native/` → `runtimes/{RuntimeIdentifier}/native/` → 应用根目录（RID 专属发布会把原生资产拍平）→ `updater/{rid}/`（手工投放）。
- **等待主程序退出**按 `restartPath` 的文件名匹配进程（`WaitForMainProcessExitSeconds` 秒，缺省回落 `restart.waitForExitMs`，超时强制继续）——同名多实例需全部退出，属已知口径。
- 更新器由 CI 以 Native AOT 按 6 RID 发布，SDK 打包时经 `UpdaterArtifactsDir`（默认仓库 `artifacts/updater/`，布局 `{rid}/native/…`）内嵌；该目录缺失时产物是"不含更新器的包"，运行期回落到上面的调度降级/`UPDATER_NOT_FOUND` 路径。

### 5.3 回传

- 更新器直接调用 `POST /api/report/install`（重试 3 次指数退避，超时 15s）；失败落状态文件 `reported=false`，SDK 启动时读取状态文件与 pending-reports 补报。
- 内容：projectKey / appId / installationId / machineFingerprint / fromVersion / toVersion / stage / installMode / elevated / elevationRejected / errorCode / errorMessage / logPath / timestamp。
- 更新器不假设网络可用，回传失败不影响安装结果。

实现要点：

- **`report.enabled=false` 由更新器直接把状态写成 `reported=true`**（本次安装不该补报），与"回传失败保留 `reported=false`"区分开；`fallbackToLocal` 只影响日志措辞，不改变该结论。
- **SDK 补报实现**：`AtomUpdater.ReportUpdaterStateAsync` 回读 `state/{appId}/updater-state.json`（契约路径固定，见 §3.14），`reported=false` 时按 §5.3 内容组装 `InstallReport` 上报，并回写 `reported=true`。缺省项回落：`projectKey`/`appId` 取 SDK 配置，`installationId` 取 `InstallationIdentity.GetOrCreate`，`machineFingerprint` 原样透传（可为 null）。
- **顺序**：状态回读补报先于 pending-reports 队列冲刷（更新器从不写队列目录，因此两者不会双投）；整个回读只受 `Report.Enabled` 闸门约束，不受 `Report.ReportOnStartup` 影响。
- **`reported=true` 的写回时机**：已送达 **或** 已入本地补报队列（`Report.FallbackToLocal && Cache.EnableOfflineReport`，此时交付责任转交给队列）；两者都不成立则保留 `reported=false`，下次启动重试。回写失败只记 Warning。
- stage/installMode 字符串→枚举映射在 SDK 侧：`verifying`/`installing`/`completed`/`failed`/`rolledBack`，未识别值归 `starting`；installMode 未识别归 `onRestart`。状态文件字段镜像由 `ConsistencyTests.SdkUpdaterStateFile_MirrorsUpdaterState` 反射守护（更新器加字段而 SDK 不认识时，回读会静默丢信息，靠该测试提醒同步）。

服务端 `POST /api/report/install` 已实现幂等——唯一索引 `(AppId, InstallationId, ToVersion, Stage, ErrorCode)`（`AreNullsDistinct(false)`），重复直传/补报返回 `200 {accepted:true, duplicate:true}` 且不插新行；更新器回传体由 `UpdaterJsonContext`（source-gen，**camelCase** + null 省略）序列化，字段名与服务端 `InstallReport` 契约一致。

## 6. 更新器分发

- 形态：**Native AOT** 单文件，单 RID 体积约 7~8MB（win-x64 实测 ≈7.74MB）；6 个 RID（win-x64/arm64、osx-x64/arm64、linux-x64/arm64）。AOT 兼容性约束：JSON 序列化用 source-gen、禁反射动态加载；osx RID 需 macOS 构建机。
- 默认：NuGet 内嵌 + SDK 首次运行释放到版本化目录（版本化避免覆盖正在运行的更新器）。
- 可选：独立分发（SDK 从服务端下载），二阶段。

实现要点：

- CI 以 `dotnet publish -p:PublishAot=true` 按 6 RID 构建（win-x64/arm64 走 windows runner，linux-* 走 ubuntu，osx-* 走 macos），产物归一到 `artifacts/updater/{rid}/native/` 后由 `dotnet pack` 内嵌进 `AtomUpdate` 包（`runtimes/{rid}/native/`，`.pdb` 排除）。
- **实测体积**：win-x64 AOT 单文件 `AtomUpdate.Updater.exe` ≈ **7.74 MB**；包体相应增大（每 RID 一份，6 RID 全内嵌时约 +40MB，可按需拆分 RID 包）。
- 本地/离线构建可用 `UpdaterArtifactsDir` 指向已有产物目录；目录缺失只影响包内容，不阻断 pack/build（§5.2 的降级路径）。

## 7. 平台适配矩阵（MVP）

| 能力 | Windows | macOS | Linux | 控制台/服务 |
|---|---|---|---|---|
| 通知 | Toast（默认） | UNUserNotification（二阶段，MVP 回落 Console/日志） | libnotify（预留） | Console/日志 |
| 提权 | runas | osascript | pkexec/sudo | 同左 |
| 缓存/状态路径 | %LocalAppData% | ~/Library/Application Support | ~/.config | 同桌面 |
| 生命周期 | AppDomain + SessionEnding | NSApplicationDelegate（宿主转发） | POSIX 信号 | IHostedService |
| 平台探测 | WMI/Registry | sysctl/defaults | /proc、/etc | - |

## 8. 测试要点

| 层级 | 覆盖 |
|---|---|
| SDK 单元测试 | 配置加载与合并、策略缓存与合并、检查/下载/校验逻辑、状态机、事件触发 |
| 更新器单元测试 | 参数解析、配置合并、SHA256、glob 匹配、替换策略、钩子、状态文件 |
| 集成测试 | Mock API 交互、SDK↔更新器交互、文件系统、网络；zip/exe 安装、提权、回滚、自升级、互斥 |
| UI 测试 | Console / WPF / MAUI（MVP）；WinForms / Avalonia 二阶段 |
| 跨平台 | Windows x64/arm64、macOS x64/arm64、Linux x64/arm64 |
| 异常场景 | 网络中断、服务器错误、包损坏、磁盘空间不足、权限不足、更新器缺失、提权被拒、主程序启动失败 |
| 性能 | 大包安装、大量文件替换、并发互斥 |

测试现状：

- **更新器单测**（`tests/AtomUpdate.Tests.Updater`）：CLI 解析、五层配置合并与 verification 锁定、TaskValidation、SHA256/签名与 delta 清单校验、glob/替换/回滚、钩子、状态文件与互斥、自升级版本比较、启动配置清理，以及 `ConsistencyTests`——用反射/round-trip 守护"更新器不引用 Core/SDK"这一前提下的三份跨组件契约（ExitCodes 常量、`UpdaterLaunchConfig`↔`UpdaterTaskFile`、`UpdaterStateFile`↔`UpdaterState`）。
- **SDK 单测**（`tests/AtomUpdate.Tests.Sdk`）含 `UpdaterIntegrationTests`：RID 映射、内嵌资产探测顺序、释放与幂等与 Unix 执行位、启动配置逐字段映射、状态回读补报的送达/入队/保留三种分支。
- 具体用例数随迭代变动，以 `dotnet test` 实际结果为准（不在文档内固化数字）。
- **端到端冒烟**：脚本化真二进制验证（`--help`/参数错误/坏 SHA/dry-run/zip 合并安装/delta 基线不匹配/delta 安装+钩子+回传关闭/`keepBackupOnSuccess`/日志文件），Windows 实测；其余 RID 由 CI 的 AOT 构建保证可编译发布，运行期未实测。
- 提权被拒（0x1003）、互斥占用（0x1001）、回滚失败（0x1009）等需要交互或多进程的路径以单测覆盖，未做真机 UAC 自动化。
