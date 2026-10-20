# 08 SDK 接入指南

> 面向宿主应用开发者：最少 4 个配置键接入检查/下载/安装/上报全链路。
> 设计细节见 `docs/04-客户端SDK与更新器设计.md`；更新器侧配置见 `docs/10-更新器配置参考.md`。

## 1. 安装

```powershell
dotnet add package AtomUpdate   # net11.0，自封包含标准更新器
```

## 2. 最小接入（Generic Host）

```csharp
using AtomUpdate.Abstractions.Interfaces;   // IAtomUpdater
using AtomUpdate.Core.Enums;                // UpdateTrigger
using AtomUpdate.DependencyInjection;       // AddAtomUpdate
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAtomUpdate(options =>
{
    // 仅四个必填键
    options.ServerUrl = "https://update.example.com";
    options.ProjectKey = "myproj";          // 管理后台项目 key
    options.AppId = "com.example.myapp";    // 应用标识
    options.CurrentVersion = "1.0.0";
    // /api/check 为公开端点，无需任何密钥
});

using var host = builder.Build();
await host.StartAsync();

var updater = host.Services.GetRequiredService<IAtomUpdater>();
var result = await updater.CheckAndDownloadAsync(UpdateTrigger.Manual);
if (result.Check.HasUpdate && result.Download.Success)
{
    await updater.InstallAndRestartAsync();   // 调度安装并重启主程序
}
```

可运行示例：`samples/AtomUpdate.Sample.Console`（命令行覆盖 `--server/--project-key/--app-id/--version`）。

## 3. 配置

配置节名 `AtomUpdate`（`AtomUpdateOptions.SectionName`）。优先级：**内置默认 → appsettings.json → `ATOMUPDATE_` 环境变量（嵌套用 `__`，如 `ATOMUPDATE_REPORT__ENABLED=false`）→ 代码覆盖 → 服务端下发策略（仅策略类字段；`Verify.RequireSignature` 等安全开关不被下发覆盖，防降级）**。

### 3.1 顶层键

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `ServerUrl` | string | 必填 | 更新服务器地址 |
| `ProjectKey` | string | 必填 | 项目 key |
| `AppId` | string | 必填 | 应用标识（状态/缓存/互斥按它隔离） |
| `CurrentVersion` | string | 必填 | 主程序当前版本 |
| `Channel` | enum | `Stable` | stable/beta/rc/preview/dev |

### 3.2 Polling 节（后台轮询）

| 键 | 默认 | 说明 |
|---|---|---|
| `Enabled` | true | 总开关 |
| `CheckOnStartup` | true | 启动后检查 |
| `InitialDelay` | 5s | 启动首查延迟 |
| `Interval` | 6h | 轮询间隔 |
| `JitterMinutes` | 10 | 抖动 ± 分钟 |
| `CheckOnNetworkRecovery` / `CheckOnResume` | true | 网络恢复/系统唤醒触发 |
| `BackoffInitial` / `BackoffMax` / `BackoffFactor` | 1min / 30min / 2 | 失败退避 |

### 3.3 Download 节

| 键 | 默认 | 说明 |
|---|---|---|
| `ModeByTrigger` | 见下 | 六触发场景 → 下载模式映射 |
| `AllowMeteredNetwork` | false | 计量网络禁止下载 |
| `MaxConcurrentDownloads` | 1 | 并发下载数 |
| `MaxDownloadSpeedKBps` | 0 | 限速 KB/s（0 不限） |
| `PauseOnLowBattery` | true | 低电量暂停 |
| `RetryCount` | 3 | 失败重试 |
| `StallTimeoutSeconds` | 60 | 无字节进展超时（0 关闭） |
| `TempDirectory` | null | 默认 `cache/{appId}/downloads/` |

默认模式映射：`Startup/Manual/Force → Foreground`（前台，不可取消）；`Polling/NetworkRecovery/Resume → Silent`（静默后台）。

### 3.4 Verify 节

| 键 | 默认 | 说明 |
|---|---|---|
| `RequireSignature` | false | true 后未签名包直接失败；**仅本地配置，服务端策略不可覆盖**。SHA256 始终强制 |

### 3.5 Install 节

| 键 | 默认 | 说明 |
|---|---|---|
| `Mode` | `OnRestart` | Manual / OnRestart / Immediate |
| `AllowPostpone` / `MaxPostponeCount` / `PostponeDeadline` | true / 3 / null | 推迟；超次/超期进入强制路径 |
| `AllowDowngrade` | false | 允许降级安装 |
| `RestartArgs` | `--skip-update-check` | 重启参数（防"检查→安装→重启→再检查"循环） |

### 3.6 Notify 节

| 键 | 默认 | 说明 |
|---|---|---|
| `Mode` | `Auto` | Auto/Console/None（MVP 默认实现 Console） |
| `ShowReleaseNotes` / `ShowOnReady` / `ShowOnDownloadFailed` | true / true / false | 通知内容开关 |
| `TitleTemplate` / `BodyTemplate` | `发现新版本 {version}` / null | 模板 |

### 3.7 Cache 节

| 键 | 默认 | 说明 |
|---|---|---|
| `EnablePolicyCache` | true | 服务端下发策略缓存 |
| `EnableETag` | true | 304 条件请求 |
| `EnableOfflineReport` | true | 上报失败落盘待补报 |
| `CacheDirectory` / `CacheExpiration` | null / null | 默认平台目录；策略缓存过期 |

### 3.8 Updater 节

| 键 | 默认 | 说明 |
|---|---|---|
| `UpdaterPath` | null | 自定义更新器路径（null = 释放内嵌更新器到版本化目录） |
| `AutoExtractUpdater` | true | 自动释放内嵌更新器 |
| `WaitForMainProcessExit` | 5 | 更新器等待主程序退出秒数 |

### 3.9 Report 节

| 键 | 默认 | 说明 |
|---|---|---|
| `Enabled` | true | 上报总开关 |
| `ReportOnStartup` | true | 启动活跃回执 + 补报队列冲刷 |
| `ReportMachineFingerprint` / `ReportInstallationId` | true | 指纹（sha256）/安装 ID 上报 |
| `RetryCount` / `Timeout` | 3 / 15s | 重试与超时 |
| `FallbackToLocal` | true | 失败落 `state/{appId}/pending-reports/`，下次启动补报 |

### 3.10 Network / Logging 节

| 键 | 默认 | 说明 |
|---|---|---|
| `Network.Proxy` | null | 代理（null = 系统默认） |
| `Network.UseDefaultCredentials` | false | 使用系统默认凭据（NTLM/Kerberos 代理场景） |
| `Network.Timeout` / `Network.RetryCount` | 30s / 3 | 检查/上报超时（下载用无进展超时） |
| `Network.UserAgent` | null | 默认 `AtomUpdate/0.1.0 ({os}; {arch})`（版本段为固定串，非程序集版本） |
| `Logging.MinLevel` / `LogToFile` / `LogDirectory` / `RetentionDays` | Information / false / null / 7 | 日志 |

## 4. IAtomUpdater 成员与事件

方法（均不阻塞主线程、可取消、返回结果对象）：

| 成员 | 说明 |
|---|---|
| `State` / `CurrentUpdate` | 状态机当前状态 / 当前可用更新 |
| `CheckAsync(trigger)` | 检查更新 |
| `DownloadAsync(mode?)` | 下载（默认按触发场景映射模式） |
| `CheckAndDownloadAsync(trigger)` | 检查 + 下载组合 |
| `InstallAndRestartAsync()` | 调度安装并重启（OnRestart 写调度状态由宿主退出触发；Immediate 立即启动更新器） |
| `Postpone(duration)` | 推迟（强制更新不可推迟） |
| `CheckManuallyAsync()` | 手动检查入口 |
| `ReportStartupAsync()` | 启动上报 + 冲刷补报队列（启动服务自动调，宿主可手动补） |

事件（统一后台线程触发）：`CheckCompleted`、`UpdateAvailable`、`DownloadStarted`、`DownloadProgressChanged`、`DownloadCompleted`、`DownloadFailed`、`UpdateReady`、`InstallStarted`、`InstallCompleted`、`InstallFailed`、`PolicyChanged`、`StateChanged`。

## 5. 离线与补报

- 检查失败：按 `Polling.Backoff*` 退避重试；策略缓存 + ETag 兜底（304 时零传输用缓存结论）。
- 上报失败：落 `state/{appId}/pending-reports/*.json`，下次启动由启动服务批量冲刷到 `POST /api/stats/batch`；服务端幂等（安装上报唯一键去重，重放返回 `duplicate=true` 不双计）。
- 更新器安装结果上报失败：落状态文件 `reported=false`，SDK 启动时读取补报（10 §状态文件）。

## 6. 服务型宿主提示

控制台形态直接可用；Windows 服务型宿主加引用 `Microsoft.Extensions.Hosting.WindowsServices` 并 `builder.Services.AddWindowsService()`，业务代码无需改动（无 UI 宿主通知本就是 Console）。

## 7. 场景一：引用 AtomUpdate 的桌面软件（WPF / WinForms / Avalonia）

桌面软件是 SDK 的主场景：Generic Host 注册服务，UI 层订阅事件驱动交互（弹窗提示 → 前台下载进度 → 就绪后重启安装）。

```csharp
// App.xaml.cs / Program.cs —— 宿主构建
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWpfBlazorWebView();          // 或其它 UI 服务
builder.Services.AddAtomUpdate(o =>
{
    o.ServerUrl = "https://update.example.com"; // 更新服务器
    o.ProjectKey = "myproj";
    o.AppId = "com.example.desktop";
    o.CurrentVersion = "1.2.0";                 // 与发布版本一致（SemVer）
    o.Channel = UpdateChannel.Stable;
});
```

```csharp
// 主窗口 ViewModel —— 事件驱动交互
public class UpdateViewModel : IDisposable
{
    private readonly IAtomUpdater _updater;
    public UpdateViewModel(IAtomUpdater updater)
    {
        _updater = updater;
        // 事件在后台线程触发，跨线程需调度回 UI（Dispatcher.Invoke / Avalonia Dispatcher.UIThread.Post）
        _updater.UpdateAvailable += (s, e) =>
        {
            var info = e.Update;                        // 版本、大小、更新说明
            if (info.ForceUpdate)
                ShowForceUpdateDialog(info);            // 强更：无"稍后"按钮
            else
                ShowUpdateDialog(info, onAccept: () => _ = RunUpdateAsync(),
                                          onPostpone: () => _updater.Postpone(TimeSpan.FromDays(1)));
        };
        _updater.DownloadProgressChanged += (s, e) => ReportProgress(e.Progress.Percent);
        _updater.UpdateReady += (s, e) => PromptRestart();   // 包已就绪待安装
        _updater.InstallCompleted += (s, e) => Environment.Exit(0); // OnRestart 模式由宿主退出触发
    }

    private async Task RunUpdateAsync()
    {
        var dl = await _updater.DownloadAsync(DownloadMode.Foreground);
        if (!dl.Success) { ShowError(dl.ErrorMessage); return; }
        await _updater.InstallAndRestartAsync();       // 调度安装：OnRestart 写状态随宿主退出生效 / Immediate 立即启动更新器
    }

    // 启动后手动检查入口（帮助-检查更新）
    public Task CheckNowAsync() => _updater.CheckManuallyAsync();
    public void Dispose() { /* 退订事件 */ }
}
```

要点：

- 后台轮询默认开启（6 小时 ± 抖动、启动 5 秒后首查、失败指数退避），无需手写定时器；轮询发现更新走 Silent 下载，就绪后 `UpdateReady` 提示
- `Postpone` 的截止时间与次数持久化，超期/超次自动升级为强制路径；服务端强更（`ForceUpdate=true`）下推迟无效，UI 必须隐藏"稍后"
- 更新器（AOT 单文件）由 `UpdaterProvisioner` 自动释放到 `{AtomUpdateRoot}/updater/{version}/`（版本化目录 + `current` 指针，避免覆盖运行中的更新器；平台目录见 [10 §平台目录](10-更新器配置参考.md)），宿主无需分发额外文件
- 仅当需要**裸更新器二进制**时（自定义安装流程、CI 缓存、内网离线分发）才去 GitHub Release 页按 RID 下载 `AtomUpdate.Updater.{rid}.zip`（同页附 `SHA256SUMS` 校验）。已引用 `AtomUpdate` 包的宿主**不要**手工放置该二进制：内嵌版本与 SDK 版本严格一致，错配会导致释放回退与自升级错配
- 全量配置项见 §3（下载模式、计量网络、限速、校验、安装模式、通知）

## 8. 场景二：引用 AtomUpdate 的 App（MAUI / 移动端）

MAUI 等 .NET App 宿主 API 完全一致，差异集中在**安装模型**：移动端没有"替换 exe 再重启"的通用机制，推荐 `OnRestart` 调度 + 宿主自行落盘应用，或仅在 UI 提示引导到应用市场。

```csharp
// MauiProgram.cs
builder.Services.AddAtomUpdate(o =>
{
    o.ServerUrl = "https://update.example.com";
    o.ProjectKey = "myproj";
    o.AppId = "com.example.mobile";          // 打包工具里应用平台选 android / ios / harmony
    o.CurrentVersion = AppInfo.VersionString;
    o.Channel = UpdateChannel.Stable;
    o.Polling.Interval = TimeSpan.FromHours(12);   // 移动端放宽轮询，省电省流量
    o.Download.AllowMeteredNetwork = false;        // 默认即禁止计量网络
});

// 检查 → 提示（移动端典型：仅提示 + 跳转，不自动安装）
var result = await updater.CheckManuallyAsync();
if (result.HasUpdate && result.Update is { } info)
{
    if (info.ForceUpdate)
        await Shell.Current.DisplayAlert("必须更新", $"最新版本 {info.LatestVersion}，请立即更新", "去更新");
    else
        await Shell.Current.DisplayAlert("发现新版本",
            $"{info.LatestVersion}\n{info.ReleaseNotes?.New?.FirstOrDefault()}", "立即更新", "稍后");
}
```

要点：

- 检查 / 下载 / 校验 / 上报链路与桌面完全相同；`DownloadAsync` 把包下到应用沙箱目录，交给宿主按平台落地（Android 安装 APK、HarmonyOS/ios 引导市场）
- 若发布渠道是应用市场，服务端可只把市场页地址写进更新说明；check 的 `ForceUpdate` / `MinSupportedVersion` 语义不变
- 补报队列在 App 冷启动时自动冲刷（§5），弱网离线检查依赖策略缓存 + ETag（304 零传输）

## 9. 场景三：其他语言接入（Java 等，REST 直连）

非 .NET 客户端不引用 SDK，直接调用公开端点（本例用 `POST /api/check` 与 `POST /api/report/install`；**无需任何鉴权头**——check/report/stats 端点公开，匿名请求按请求体 projectKey/appId 反查租户）。字段名 camelCase，完整契约见 [contracts/openapi.v1.json](contracts/openapi.v1.json)。

### 9.1 契约

**POST /api/check** —— 请求体：

```json
{
  "projectKey": "myproj",
  "appId": "com.example.java",
  "currentVersion": "1.2.0",
  "platform": "windows",
  "arch": "x64",
  "channel": "stable",
  "installationId": "uuid-每台机器固定",
  "buildNumber": 123,
  "policyVersion": 0
}
```

- 响应头 `ETag`：下次检查带 `If-None-Match` 命中即 304（无响应体，用上次缓存的结论）
- 响应体（有更新时）：

```json
{
  "hasUpdate": true,
  "latestVersion": "1.3.0",
  "forceUpdate": false,
  "minSupportedVersion": null,
  "policyUnchanged": false,
  "policy": { "version": 2, "allowDowngrade": false, "allowForceUpdate": true },
  "package": {
    "type": "delta",
    "packageType": "zip",
    "fromVersion": "1.2.0",
    "url": "https://update.example.com/uploads/app/1.3.0/app-1.3.0-x64.zip",
    "sha256": "…",
    "size": 15728640,
    "arch": "x64"
  },
  "fullPackage": {
    "packageType": "zip",
    "url": "https://update.example.com/uploads/app/1.3.0/app-1.3.0-windows-x64.zip",
    "sha256": "…",
    "size": 52428800,
    "arch": "x64"
  },
  "releaseNotes": { "new": ["…"], "improvements": ["…"], "fixes": ["…"], "notes": [] }
}
```

- `hasUpdate=false` 时只有 `latestVersion` / `policyUnchanged` / `policy`
- `package` 为优先包（增量优先），`fullPackage` 始终作为兜底下发；`package.type=delta` 时其 `fromVersion` 必须等于请求里的当前版本才可应用，否则回落 `fullPackage`；`forceUpdate=true` 时客户端不得提供跳过选项
- `releaseNotes` 无 `language` 字段（服务端按 en 优先取一种语言下发）
- 客户端职责：下载 → SHA-256 校验（不匹配丢弃重试）→ 应用包（zip 解压替换 / exe 静默安装，`package` 上的 silent/install 参数与打包时填写一致）→ 上报

**POST /api/report/install** —— 安装结果上报（幂等，重放返回 `duplicate=true`）：

```json
{
  "projectKey": "myproj",
  "appId": "com.example.java",
  "installationId": "uuid",
  "fromVersion": "1.2.0",
  "toVersion": "1.3.0",
  "stage": "completed",
  "installMode": "onRestart"
}
```

失败上报：`stage` 置 `rolledBack`，附 `errorCode` / `errorMessage`；离线期间的上报落盘后补发即可（服务端幂等去重）。

### 9.2 Java 示例（JDK 17+ HttpClient，JSON 解析以 Gson 为例）

```java
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.net.URI;
import java.net.http.*;
import java.nio.file.*;
import java.security.MessageDigest;
import java.time.Duration;
import java.util.HexFormat;

public class AtomUpdateClient {
    private static final String SERVER = "https://update.example.com";
    private static final String PROJECT_KEY = "myproj";            // 与发布时一致
    private static final String INSTALLATION_ID = "固定 uuid";      // 每台机器持久化一次
    private static final String APP_ID = "com.example.java";

    private final HttpClient http = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10)).build();

    /** 检查 + 下载 + 校验，返回包路径；无更新返回 null。 */
    public Path checkAndDownload(String currentVersion) throws Exception {
        String body = """
            {"projectKey":"%s","appId":"%s","currentVersion":"%s",
             "platform":"windows","arch":"x64","channel":"stable",
             "installationId":"%s","policyVersion":0}
            """.formatted(PROJECT_KEY, APP_ID, currentVersion, INSTALLATION_ID);

        HttpRequest check = HttpRequest.newBuilder(URI.create(SERVER + "/api/check"))
            .header("Content-Type", "application/json")
            .POST(HttpRequest.BodyPublishers.ofString(body)).build();

        HttpResponse<String> resp = http.send(check, HttpResponse.BodyHandlers.ofString());
        if (resp.statusCode() == 304) return null;                 // ETag 命中：用上次缓存结论
        if (resp.statusCode() != 200) throw new IllegalStateException("check HTTP " + resp.statusCode());

        JsonObject json = JsonParser.parseString(resp.body()).getAsJsonObject();
        if (!json.get("hasUpdate").getAsBoolean()) return null;

        if (json.get("forceUpdate").getAsBoolean()) {
            // 强更：UI 不得提供跳过选项；此处直接进入下载安装
        }
        JsonObject pkg = json.getAsJsonObject("package");
        // type=delta 的前提是 fromVersion 与当前版本一致，否则应回落 fullPackage（本例简化：仅应用服务端给的 package）
        Path target = Files.createTempFile("update-", ".zip");
        http.send(HttpRequest.newBuilder(URI.create(pkg.get("url").getAsString())).build(),
                  HttpResponse.BodyHandlers.ofFile(target));

        if (!sha256(target).equalsIgnoreCase(pkg.get("sha256").getAsString()))
            throw new SecurityException("SHA-256 mismatch");       // 丢弃并重试/回落全量

        // 交给宿主应用包：解压替换主程序目录 / 运行静默安装程序（结合自身部署形态）
        applyPackage(target);
        reportInstall(currentVersion, json.get("latestVersion").getAsString());
        return target;
    }

    private void reportInstall(String from, String to) {
        String body = """
            {"projectKey":"%s","appId":"%s","installationId":"%s","fromVersion":"%s","toVersion":"%s",
             "stage":"completed","installMode":"onRestart"}
            """.formatted(PROJECT_KEY, APP_ID, INSTALLATION_ID, from, to);
        HttpRequest report = HttpRequest.newBuilder(URI.create(SERVER + "/api/report/install"))
            .header("Content-Type", "application/json")
            .POST(HttpRequest.BodyPublishers.ofString(body)).build();
        try { http.send(report, HttpResponse.BodyHandlers.discarding()); }
        catch (Exception e) { /* 落盘待下次启动补发（服务端幂等） */ }
    }

    private static String sha256(Path file) throws Exception {
        var digest = MessageDigest.getInstance("SHA-256");
        try (var in = Files.newInputStream(file)) {
            byte[] buf = new byte[1 << 16];
            for (int n; (n = in.read(buf)) > 0; ) digest.update(buf, 0, n);
        }
        return HexFormat.of().formatHex(digest.digest());
    }

    private void applyPackage(Path zip) throws Exception { /* 平台相关：解压替换 / 静默安装 */ }
}
```

### 9.3 快速验证（curl）

```bash
curl -s -X POST https://update.example.com/api/check \
  -H "Content-Type: application/json" \
  -d '{"projectKey":"myproj","appId":"com.example.java","currentVersion":"1.2.0",
       "platform":"windows","arch":"x64","channel":"stable",
       "installationId":"11111111-1111-1111-1111-111111111111","policyVersion":0}'
```

Go / Python / Rust / Electron 等语言按同一契约实现即可，工作量集中在三处：check 调用（含 ETag 缓存与 304）、SHA-256 校验、安装后上报的落盘补发。
