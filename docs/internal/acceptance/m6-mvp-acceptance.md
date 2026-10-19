# M6 MVP 验收清单核对报告

> 依据 `docs/06-里程碑开发计划.md` §4 验收映射表（需求 §30 节选）逐条核对。
> 核对日期：2026-10-08；全量测试 380+ 全绿；Release 构建 0 警告 0 错误；端到端实测见 [e2e runbook](../e2e/m6-e2e-runbook.md)。

## 验收映射核对

| # | 验收项 | 里程碑 | 证据 | 状态 |
|---|---|---|---|---|
| 1 | 简单配置即可检查更新、四平台兼容 | M3 | 最小接入仅 4 个必填键（ServerUrl/ProjectKey/AppId/CurrentVersion），见 [08-SDK接入指南](../../08-SDK接入指南.md)；平台路径解析覆盖 Windows/macOS/Linux（`PlatformPaths`/`UpdaterPaths`），`AtomUpdate.Tests.Sdk` 全绿；端到端实测 Windows x64（[runbook](../e2e/m6-e2e-runbook.md) §3） | ✅ |
| 2 | 通道体系、zip/exe 包 | M1/M3 | stable/beta/rc/preview/dev 五通道（`UpdateChannel`），发布按通道解析资产；Packager `--type zip/exe`；runbook §2 发布至 stable 并被客户端选中 | ✅ |
| 3 | 前台下载不可取消/后台/跳过、静默下载、强制更新阻止启动 | M3/M4 | `DownloadOptions.ModeByTrigger` 六触发场景默认模式（Startup/Manual/Force=Foreground，Polling/NetworkRecovery/Resume=Silent）；跳过版本与强制更新由策略层执行；`AtomUpdate.Tests.Sdk` / `AtomUpdate.Tests.Updater` 覆盖 | ✅ |
| 4 | 更新日志结构化展示 | M3 | ReleaseNotes 结构化（new/improvements/fixes + 语言），随检查响应分层下发（`docs/03` §3）；Packager `--note-new/--note-improvements/--note-fixes/--notes-file` 写入 | ✅ |
| 5 | Packager 上传第三方存储并推送、API 只存元数据 | M5/M1 | 资产落 local/S3 存储（`StorageUploaderBase` 派生），Api 仅存 URL/哈希/大小元数据；runbook §2 实测：zip 经 nginx HTTP 分发，Api 侧仅元数据 | ✅ |
| 6 | 一键打包上传发布 | M5 | `publish` 单命令完成 pack→delta→签名→上传→推送→发布；runbook §2（v1.0.0 全量、v1.1.0 全量+增量） | ✅ |
| 7 | 多项目隔离 | M1 | 全实体 `ITenantScoped` 全局查询过滤器 + 项目/应用作用域 ApiKey；`AtomUpdate.Tests.Api` 租户隔离用例 | ✅ |
| 8 | 安装统计（机器指纹去重） | M6 | `installations/summary`：total / distinctFingerprints / withoutFingerprint / 活跃 24h·7d / 版本·通道分布；runbook §3 实测 total=1、去重=1；`StatsEndpointTests` 数值断言 | ✅ |
| 9 | 更新失败回滚、钩子、替换策略 | M4 | `RollbackSection`（OnFailure/KeepBackupOnSuccess）、`InstallSection.Hooks`（abort/continue）、merge/replace + DeleteOrphans；`AtomUpdate.Tests.Updater` 覆盖 | ✅ |
| 10 | 提权强制授权、双进程自升级、互斥、退出码/状态文件协议 | M4 | `ElevationSection`（onDemand/always/never + Retry）、`SelfUpdateSection.Mode=dualProcess`、按 appId 命名互斥（0x1001）；退出码 0x0000/0x1001–0x100B 与状态文件 `state/{appId}/updater-state.json`，见 [10-更新器配置参考](../../10-更新器配置参考.md) | ✅ |
| 11 | 全局互斥、三种通知（*MVP 一种默认实现） | M4/M3 | 互斥：`updater.globalMutex` + `mutexTimeoutSeconds`；通知：`NotifyMode`（Auto/Console/None，Console 为 MVP 默认实现，余两种归二阶段） | ✅ |
| 12 | ETag/304、policyVersion、响应分层 | M1/M3 | runbook §4 实测：第二次检查 ETagHit=t，`checks/summary` eTagHitRate7d=0.5；`UpdateCheckLogs` 1 miss + 1 hit；policyVersion 随响应下发并触发 `PolicyChanged` | ✅ |
| 13 | PostgreSQL、Blazor Auto、NeoUI.Blazor、FluentValidation | M1/M2 | EF Core + Npgsql（compose `postgres:16-alpine`）；Web 为 Blazor Auto 单进程托管 Api；UI 组件 NeoUI.Blazor；manage 写入端点 FluentValidation | ✅ |

## M6 自身出口条件

| 出口项 | 证据 | 状态 |
|---|---|---|
| 上报接入端点全部实现（install/download/report/batch） | `ReportEndpoints`；`ReportEndpointTests` 67 项 Api 套件内全绿；runbook §3/§5 | ✅ |
| 统计页（指纹去重、304/缓存命中率、版本分布、下载成功率） | Web `/stats` 页；四个 summary 端点实测（runbook §3/§4） | ✅ |
| `/api/check` < 200ms 压测报告 | [m6-check-benchmark.md](../perf/m6-check-benchmark.md)：P95≈13ms（200 与 304 双路径） | ✅ |
| 私有化部署 docker-compose | `docker-compose.yml` + `src/AtomUpdate.Web/Dockerfile`；CI `docker-build` 冒烟；compose 栈实测（runbook 全程） | ✅ |
| 端到端联调（含断网补报幂等、delta 链路） | runbook §5：补报 3 条、Installations 不双计、`/api/report/install` 重放 duplicate=true 单行；§6：`DownloadLogs.Kind=Delta` | ✅ |
| 文档三件 | [08](../../08-SDK接入指南.md) / [09](../../09-部署手册.md) / [10](../../10-更新器配置参考.md) | ✅ |

## 结论

需求 §30 MVP 相关验收项 13/13 通过，M6 出口条件 6/6 通过。**MVP 达成发布条件**（NuGet 包已于 M3–M5 就绪，Api 部署镜像 = compose 本地构建，推送 registry 归二阶段）。
