# 03 数据模型与 API 设计

## 1. 实体关系总览

```
Tenant 1─n User 1─n ProjectMember n─1 Project
Project 1─n App 1─n Release 1─n ReleaseAsset
Release n─n UpdateChannel   (ReleaseChannel: release × channel × status；通道为全局枚举预设)
Release 1─n ReleaseNote (语言维度)
App 1─1 UpdatePolicy (含 version 自增)   App 1─n ClientPolicyOverride
App 1─n Installation (installationId, machineFingerprint, channel)
Installation 1─n InstallationReport / UpdateCheckLog / DownloadLog
User/Project/Release 操作 → AuditLog
PackageSource：记录资产来源（存储标识、区域），不含凭据
```

## 2. 核心实体字段设计

> **主键约定**：所有服务端实体 `Id` 均为 `long`，由 **IdGen 库**（NuGet `IdGen`，Snowflake 结构：41 时间位 + 10 生成器位 + 12 序列位）生成——时间有序、趋势递增、分布式无中心。Api 直接 `AddSingleton(new IdGenerator(generatorId))` 并桥接注册 `IIdGenerator<long>` 单例；库不含 EF Core / JSON 配套包，由 Api 自实现 EF Core `ValueGenerator<long>`（`Add` 时应用侧赋值）与 `System.Text.Json` `JsonConverter<long>` 两个薄封装。多实例部署需保证每实例 `GeneratorId` 唯一（`IdGen:GeneratorId` 配置注入；单实例默认 0）。例外：`Installation.Id /*=installationId*/` 为客户端生成的匿名 UUID，不适用 IdGen。跨 JSON 边界时 `Id` 以字符串输出（防 JS `Number` 安全整数范围外的精度丢失），数据库与 .NET 内部保持 `long`。

### 2.1 租户与权限域

```csharp
Tenant        { Id, Name, Slug, Status, CreatedAt }
User          { Id, TenantId, Email /*全局唯一*/, UserName /*可选登录名，全局唯一，存储小写*/, PasswordHash /*PBKDF2 自实现*/, DisplayName, IsPlatformAdmin /*平台管理员*/, Status }
Project       { Id, TenantId, Key /*唯一, 如"myapp"*/, Name, Description, Status }
ProjectMember { ProjectId, UserId, Role /* admin|publisher|viewer */ }   // 二阶段启用：MVP 简化两级权限
```

### 2.2 应用与发布域

```csharp
App
{
    Id, ProjectId, AppId /*"com.company.myapp"*/,
    Name, Type /* WindowsDesktop|MacDesktop|LinuxDesktop|Console|AppService|App */,
    Platform /*windows|macos|linux，创建后不可改*/,
    Status
}
// 平台是应用属性（windows|macos|linux，创建后不可改）：一个 appId 只服务一个平台，
// 类型与平台解耦（Console/AppService/App 不区分桌面平台，平台仍须显式选定）。
// 架构是资产属性：Release 自身不含 Platform/Arch——更新全程跟随 appId，
// 同一版本的语义在应用内唯一，同版本号的多架构包是同一条 Release 下的多个资产。

// 通道为全局枚举预设，管理端不可增删改——
// UpdateChannel { dev=开发版, preview=预览版, beta=公测版, rc=候选版, stable=正式稳定版 }
// （wire 值为 camelCase 全小写：dev/preview/beta/rc/stable）。发布时按枚举勾选，
// /api/check 请求的 channel 即该枚举；越界值 400。

Release                    // 版本（应用内一条版本号只有一条记录；平台来自所属应用，架构在各包上）
{
    Id, AppId, Version /*SemVer*/, BuildNumber,
    Status /*draft|prerelease|published|withdrawn|rolledback*/,
    ForceUpdate, MinSupportedVersion, PublishedAt, CreatedBy
}
// 唯一索引：(AppId, Version)

ReleaseChannel             // 版本×通道 的发布关系（二维矩阵）
{
    ReleaseId, Channel /*UpdateChannel 枚举*/,
    Status /*draft|published|withdrawn*/,
    PublishedAt, ReleaseSeq /*发布序号，bump 即缓存失效，见 02 文档 §3.1*/
}

ReleaseAsset               // 包资产（架构维度在资产上：一条版本可挂 x64/arm64/x86 多个包）
{
    Id, ReleaseId,
    Kind /*full|delta*/, FromVersion /*delta 起点版本*/,
    Arch /*x64|arm64|x86*/,
    PackageType /*zip|exe|msi|dmg|pkg|appimage|deb|rpm*/,
    Url, Size, Sha256, Signature, KeyId,    /* Signature/KeyId 可空可选（签名为可选项，见 02 文档 §6.3） */
    SilentArgs /*exe/msi*/, InstallArgs, SourceId /*PackageSource*/
}
// 唯一索引：(ReleaseId, Kind, FromVersion, PackageType, Arch)（NULL 视为相同）

ReleaseNote
{ Id, ReleaseId, Language, New[], Improvements[], Fixes[], Notes[], RawMarkdown }
    // 四段的录入约定（后台与 05 §2 Packager 向导一致）：多行文本、每行一条要点、行内 Markdown 原样存储；
    // 行首 "- " / "* " 列表符号入库前剥离。RawMarkdown 为整段兜底字段，不经 /api/check 下发

PackageSource
{ Id, AppId, Provider /*local|s3|azure|oss|cos|qiniu|http*/, Region, Bucket, DisplayName }  // 无凭据
```

### 2.3 策略域

```csharp
UpdatePolicy
{
    Id, AppId, Channel?,              // 可按应用/通道两级配置（Channel 为 UpdateChannel 枚举，通道级覆盖二阶段启用）
    Version /*int, 服务端自动+1*/,
    PollingEnabled, PollingIntervalMinutes, PollingJitterMinutes,
    DownloadOnStartup, DownloadOnPolling, DownloadOnManualCheck,
    DownloadOnForceUpdate, DownloadOnNetworkRecovery, DownloadOnResume,
    InstallMode /*manual|onRestart|immediate*/,
    AllowForceUpdate, AllowDowngrade,    /* 默认均 false */
    AllowPostpone, MaxPostponeCount, AllowMeteredNetwork, MaxDownloadSpeedKBps,
    UpdatedAt, UpdatedBy
}
```

> `policy.version` 在同一事务内随策略修改递增；**MVP 粒度为 App 级**；实体保留 `Channel?` 枚举字段与 ClientPolicyOverride 表供通道级覆盖（二阶段启用）。防降级判定：默认防降级（策略显式开启 `AllowDowngrade` + 客户端本地开关同时允许才提示回退）。

### 2.4 统计域

```csharp
Installation        { Id /*=installationId*/, AppId, MachineFingerprintHash, Channel, FirstSeenAt, LastSeenAt, LastVersion }
UpdateCheckLog      { Id, InstallationId, AppId, Platform, Arch, Channel, CurrentVersion, LatestVersion, HasUpdate, ETagHit /*bool 304*/, PolicyUnchanged, CheckedAt }
DownloadLog         { Id, InstallationId, AppId, FromVersion, ToVersion, Kind /*full|delta 可空*/, Status, SizeBytes, ErrorCode, StartedAt, FinishedAt }
InstallationReport  { Id, AppId, InstallationId, FromVersion?, ToVersion, Stage /*starting|verifying|installing|completed|failed|rolledBack*/,
                      InstallMode, Elevated, ElevationRejected, ErrorCode?, ErrorMessage?, LogPath?, Timestamp }
```

> UpdateCheckLog/DownloadLog 按月分区（PostgreSQL `PARTITION BY RANGE`），保留策略可配；写入走后台批量通道。
>
> **对齐实现口径**：①DownloadLog 按上报契约无 `ReleaseAssetId`/`Mode` 字段，`Kind`（full/delta）为可空列；分区未实施（MVP 量级不需要）。②InstallationReport 实际字段：`Stage`（starting/verifying/installing/completed/failed/rolledBack）+ `InstallMode` + 提权标记 + `ErrorCode/ErrorMessage`（截断 2000），幂等唯一索引 `(AppId, InstallationId, ToVersion, Stage, ErrorCode)` 且 `AreNullsDistinct(false)`。③Installation 增 `LastVersion`（startup/install 上报刷新）与 `(AppId, LastSeenAt)` 索引。

## 3. 更新检查 API（核心契约）

### 3.1 请求

```
POST /api/check
Headers:
  （公开端点，无需鉴权头；管理端 JWT 用 Authorization）
  User-Agent: AtomUpdate/{sdkVersion} ({os}; {arch})
  If-None-Match: "myapp-com.company.myapp-windows-x64-1.2.3-1.2.3-p12"
Body:
{
  "projectKey": "myapp",
  "appId": "com.company.myapp",
  "currentVersion": "1.2.3",
  "buildNumber": 100,
  "platform": "windows",
  "arch": "x64",
  "channel": "stable",
  "installationId": "guid",
  "machineFingerprint": "sha256-hash",
  "policyVersion": 12
}
```

> 请求不含 `environment`/`runtime` 字段（运行时信息由 User-Agent/统计域承载）；`channel` 为 UpdateChannel 枚举（dev/preview/beta/rc/stable），非法值 400 INVALID_REQUEST。

> **预留字段**：`buildNumber` 与 `machineFingerprint` 服务端不读（不参与版本比较与去重）、SDK 亦未上报——为统计域预留的契约字段。

> 是否下发降级（把更低版本作为可用更新返回）由**服务端按应用级策略 `AllowDowngrade` 判定**（默认防降级，显式开启才提示回退）；SDK 侧 `Install.AllowDowngrade`（默认 false）为本地兜底开关，在安装环节拦截——回退要实际完成下载安装，需两者同时开启。

> ETag 为**强 ETag**：`"hash16(projectKey|appId|platform|arch|currentVersion|latestVersion|p{policyVersion})"`——以 `|` 连接的规范化串（platform/arch 小写）取 SHA256 **前 8 字节的 32 位小写 hex**（16 字符不透明串，如 `"3f9c2a1b7e4d08a6"`）；组成中**加入 latestVersion**，避免"有更新但策略未变时返回 304"的漏发缺陷。

### 3.2 响应

**304 Not Modified**（响应体未变）：

```
HTTP/1.1 304 Not Modified
ETag: "3f9c2a1b7e4d08a6"    （不透明 hash16；其组成见 §3.1 说明）
```

**200 无更新且策略未变：**

```json
{ "hasUpdate": false, "latestVersion": "1.3.0", "policyUnchanged": true }
```

**200 无更新但策略已变：**（`policy` 体完整下发）

**200 有更新：**（完整结构如下）

```json
{
  "hasUpdate": true,
  "latestVersion": "1.3.0",
  "forceUpdate": false,
  "minSupportedVersion": "1.0.0",
  "policyUnchanged": false,
  "policy": {
    "version": 13,
    "pollingEnabled": true,
    "pollingIntervalMinutes": 360,
    "pollingJitterMinutes": 10,
    "downloadOnStartup": true,
    "downloadOnPolling": false,
    "downloadOnManualCheck": true,
    "downloadOnForceUpdate": true,
    "downloadOnNetworkRecovery": false,
    "downloadOnResume": false,
    "installMode": "onRestart",
    "allowForceUpdate": false,
    "allowDowngrade": false,
    "allowPostpone": true,
    "maxPostponeCount": 3,
    "allowMeteredNetwork": false,
    "maxDownloadSpeedKBps": 0,
    "updatedAt": "2026-10-06T00:00:00+00:00"
  },
  "package":      { "type": "delta|full", "packageType": "zip", "fromVersion": "1.2.3",
                    "url": "...", "size": 0, "sha256": "...", "signature": "...", "keyId": "..." },
  "fullPackage":  { "packageType": "exe", "url": "...", "sha256": "...", "signature": "...", "keyId": "...",
                    "silentArgs": "/VERYSILENT /NORESTART" },
  "releaseNotes": { "new": [], "improvements": [], "fixes": [], "notes": [] }
}
```

> **契约冻结**：请求/响应线上字段名的完整规范已冻结为 `docs/contracts/openapi.v1.json`（由 `AtomUpdate.Api` 的 `/openapi/v1.json` 导出；导出须以 Api 独立宿主为准：`ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5062 dotnet run --project src/AtomUpdate.Api --no-launch-profile` 后取 `/openapi/v1.json`；Web 宿主导出的 `info.title`/`servers` 会不同）。软约定：枚举一律 camelCase 出网（`appimage` 特例经 `JsonStringEnumMemberName` 固定为全小写）；策略字段中轮询间隔/抖动单位为分钟、限速单位为 KB/s（`maxDownloadSpeedKBps=0` 表示不限速）；`null` 字段在响应中省略（分层下发，见 `AtomUpdate.Core.Serialization.AtomUpdateJson`）。

未签名包省略 `signature`/`keyId`（客户端默认不验签、可显式启用，见 02 文档 §6.3）。

**包选择算法**（服务端；平台属于应用、架构属于包）：
1. 平台守卫：请求上报的 `platform` 必须等于应用注册的 `Platform`，否则 **400 `PLATFORM_MISMATCH`**（ AppId 用错/构建目标配错是配置错误，不静默返回"无更新"）；
2. 按 `channel` 定位该应用最新已发布 Release（应用内一条版本号只有一条记录，不再按平台/架构拆行）；
3. 按请求 `arch` 筛该版本的可用资产：**arm64 无包时回落 x64**（WoA / Rosetta 可运行），x64 客户端**不**回落到 arm64；
4. 优先匹配 `delta` 资产：`fromVersion == currentVersion`；
5. 无匹配增量 → 回落 `full` 资产；响应的 `package.type` 告知客户端实际类型，`fullPackage` 始终作为兜底随响应下发。

> 第 3 步"该架构一个包都没有"**不报错**，按 `hasUpdate=false` 返回（`latestVersion` 仍如实回填）：灰度期某架构包尚未上传时，不应把正常客户端打成错误。可观测性由 `UpdateCheckLog.Arch` 与后台版本列表的"包架构"列承担。

> **响应回传资产架构**：`package`/`fullPackage` 的 **`arch` 为必填字段**（`UpdateArch` 枚举 camelCase：`x86|x64|arm64`）；arm64 设备回落 x64 包后，SDK 以该字段（而非设备架构）填更新器验签内容（04 §5.1）。包文件经 **`POST /api/manage/projects/{projectId}/apps/{appId}/upload?version={v}`** multipart 上传（JWT Bearer，服务端落盘 StorageRoot 并计算 SHA-256，见 05 §9）。

**错误响应**：统一 `{ "code": "...", "message": "..." }`，取值见 `AtomUpdate.Core.Constants.ErrorCodes`，如 `APP_NOT_FOUND / INVALID_CHANNEL / INVALID_VERSION / PLATFORM_MISMATCH / UNAUTHORIZED`。

### 3.3 其他客户端端点

```
POST /api/stats/install        安装实例注册（SDK 启动上报 installationId）
POST /api/stats/check          检查行为上报（与 /api/check 合并记录时可省）
POST /api/stats/download       下载结果上报
POST /api/report/install       安装结果上报（更新器直传 + SDK 补报共用，见下）
POST /api/stats/batch          批量上报（SDK IUpdateReporter.ReportBatchAsync，离线队列 flush）
GET  /health                   运维探针（应用健康检查）
```

> `/api/stats/install`、`/api/stats/download`、`/api/report/install`、`/api/stats/batch` 均已落地（公开端点：匿名按请求体 projectKey/appId 反查租户，JWT 会话直接按 tenant_id 过滤）。`/api/stats/check` 不实现——batch 内 `check` 事件服务端丢弃（`/api/check` 已落 UpdateCheckLog，双计会污染 304 命中率）。上报失败 SDK 落 `pending-reports` 等下次启动补报（04 §3.14）。

`/api/report/install` 说明（对齐更新器需求 §15）：

- 更新器在安装成功 / 失败 / 回滚 / 提权被拒时**直接调用**（公开端点，无需鉴权头；重试 3 次指数退避，超时 15s）。
- 回传失败时更新器在状态文件保留 `reported=false`，主程序下次启动由 SDK 读取状态文件**补报**同一接口。
- 请求体：projectKey / appId / installationId / machineFingerprint / fromVersion / toVersion / stage / installMode / elevated / elevationRejected / errorCode / errorMessage / logPath / timestamp。
- 服务端写入 InstallationReport，按 `(installationId, toVersion, stage, errorCode)` 幂等去重（直传与补报可能重复）。

### 3.4 管理 / 发布端点（契约以 [contracts/openapi.v1.json](contracts/openapi.v1.json) 为准）

```
# release 生命周期（Packager 或管理后台；JWT Bearer，Manage 策略）
POST   /api/projects/{projectId}/apps/{appId}/releases                 创建草稿版本+资产
       （幂等键：AppId+Version——平台属于应用、架构属于包，版本号在应用内唯一；
        同键同资产 → 200 返回既有版本；
        同键不同资产或同键已撤回 → 409 RELEASE_EXISTS（禁同版本重传，撤回后须用新构建号）；
        对已撤回版本 publish → 409 RELEASE_WITHDRAWN）
PATCH  /api/projects/{projectId}/apps/{appId}/releases/{id}            编辑草稿（仅 Draft/Prerelease；
        Withdrawn/Published → 409；可改 BuildNumber/ForceUpdate/MinSupportedVersion/更新日志）
POST   /api/projects/{projectId}/apps/{appId}/releases/{id}/publish    发布（body：channels[dev|preview|beta|rc|stable] + publishedAt?）
POST   /api/projects/{projectId}/apps/{appId}/releases/{id}/unpublish  撤回（整条 Release 对所有通道行置 withdrawn，幂等）
GET    /api/projects/{projectId}/apps/{appId}/releases                 查询版本（分页 page/pageSize + 过滤 arch/status；arch=有该架构包的版本；Packager 历史页复用）
POST   /api/projects/{projectId}/apps/{appId}/releases/{id}/rollback   回滚（三阶段实现）

# 包文件上传（JWT Bearer，Manage 策略）
POST   /api/manage/projects/{projectId}/apps/{appId}/upload?version={v}  multipart 上传（字段 file），返回 {url,sha256,size,filename}

# manage（管理后台使用；除 auth 外均要求 JWT，tenants 相关需平台管理员，其余端点要求
#  tenant_id 声明且跨租户不可见）
GET/POST        /api/manage/tenants
GET/PATCH       /api/manage/tenants/{id}
GET/POST        /api/manage/projects
GET/PATCH/DELETE /api/manage/projects/{id}          （删除为受限删除体系：须先清空版本/应用并经确认门禁）
GET/POST        /api/manage/projects/{projectId}/apps      （POST 必填 platform，缺失 400 INVALID_REQUEST；创建后不可改）
GET/PATCH/DELETE /api/manage/projects/{projectId}/apps/{appId}（受限删除：须先清空版本）
GET             /api/manage/apps                    （跨项目全量列表，后台"应用"页默认视图；
        可选过滤 projectId / type（应用类型，大小写不敏感，非法值 400）/ keyword（名称或 AppId ILike）；
        响应 AppDto 附带 projectId + projectName，单项目上下文调用时 projectName 为该项目）
GET/PATCH       /api/manage/apps/{id}
GET/PATCH       /api/manage/apps/{id}/policy        （PATCH 仅实质变更才 bump policy.version）
GET/POST        /api/manage/users                   （GET 支持分页与 keyword 搜索）
PATCH/DELETE    /api/manage/users/{id}
POST            /api/manage/auth/login              （用户名或邮箱 + 密码换取 JWT）
GET             /api/manage/auth/setup/status       （公开：是否需要首次初始化）
POST            /api/manage/auth/setup              （公开：设置向导，仅用户表为空时可用，创建平台管理员并登录）
POST            /api/manage/auth/change-password    （修改当前用户密码）
GET             /api/manage/overview                （轻量概览）
GET             /api/manage/audit-logs              （分页，Id 倒序）
GET             /api/manage/releases                （跨项目版本列表，后台"全部版本"视图）
GET             /api/manage/stats/overview          （统计概览）
GET             /api/manage/stats/installations             （安装实例分页列表）
GET             /api/manage/stats/installations/summary     （按版本/通道汇总）
GET             /api/manage/stats/downloads/summary         （下载汇总）
GET             /api/manage/stats/checks/summary            （检查汇总）
```

> **鉴权规则**：`/api/check` 与上报端点（`/api/report/*`、`/api/stats/*`）**公开**——匿名请求按请求体 `projectKey` 跨租户匹配（0 命中 → 404，多重命中 → 401，不泄露存在性与归属），JWT 会话按 tenant_id 声明租户内过滤；release/上传端点统一 **Manage 策略**（JWT Bearer，调用方为打包工具登录会话与管理后台）。

## 4. ETag 与缓存实现细节

```csharp
// 逻辑响应组装后（实现见 AtomUpdate.Api/Endpoints/CheckEndpoint.cs）：
var raw = $"{projectKey}|{appId}|{platform}|{arch}|{currentVersion}|{latestVersion}|p{policyVersion}"; // platform/arch 小写
var etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)).AsSpan(0, 8)).ToLowerInvariant()}\""; // hash16
if (Request.Headers.IfNoneMatch == etag) return NotModified(etag);
Response.Headers.ETag = etag;
return Ok(body);
```

- 服务端不保存 ETag 状态，**无状态可横向扩展**；发布操作改变 latestVersion / policyVersion → 哈希自然变化 → 缓存自动失效。
- 版本比较使用 NuGet `Semver` 库；非法版本号返回 400。
- 同版本资产重传：**不允许**——包文件有误时撤回后用新构建号重发，规避 ETag 缓存一致性与 AssetRevision 复杂度。

## 5. 数据库迁移与多数据库

- EF Core Migrations，Provider：`Sqlite`（默认，零依赖单文件）/ `Npgsql`（生产推荐）。
- 多租户隔离：所有业务表带 `TenantId`，EF Core Global Query Filter 强制过滤；唯一索引均含租户列。
- 主键统一 `bigint`（SQLite `INTEGER`）：Api 引用 `IdGen`（直接注册 `IdGenerator` 单例，见 §2 主键约定），自实现 EF Core `ValueGenerator<long>` 封装生成器（`Add` 时应用侧赋值，无需数据库往返）；多实例部署每实例注入唯一 `GeneratorId`。
- 迁移位置与启动策略：EF 迁移位于 `AtomUpdate.Api/Data/Migrations`；**Development 启动自动 `MigrateAsync()`**，生产由部署流程显式 `dotnet ef database update`（版本由仓库根 `dotnet-tools.json` 固定，CI 先 `dotnet tool restore`）。
- 时间统一 UTC 存储，展示层转换。
