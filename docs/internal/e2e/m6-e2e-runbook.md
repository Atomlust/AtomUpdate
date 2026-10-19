# M6 端到端联调 Runbook

> 私有化部署形态（docker-compose）下 Api + Web + SDK + Packager 全链路演练记录。
> 演练日期：2026-10-08；环境：Windows 11 宿主机 + Docker Desktop；栈：`docker compose up -d --build`（web:8080 + postgres:16，不暴露 PG 到宿主机）。

## 0. 环境准备

| 项 | 值 |
|---|---|
|  compose 环境变量 | `JWT_SIGNING_KEY` / `BOOTSTRAP_EMAIL=admin@e2e.local` / `BOOTSTRAP_PASSWORD`（见 09-部署手册） |
| 健康检查 | `GET http://localhost:8080/health` → `{"status":"ok"}` |
| 包存储 HTTP 服务 | `docker run -d -p 8081:80 -v <localRoot>:/usr/share/nginx/html:ro nginx:alpine`（local 存储凭据的 `publicBaseUrl=http://localhost:8081`） |
| Packager 凭据 | 手写 `{work-dir}/credentials/e2e-local.json`：`type=local`、`crypto=dpapi-currentuser-v1`、`fields.localRoot/publicBaseUrl`、`protectedFields={}`（空敏感字段时不触发解密，`crypto` 仅作标记） |

## 1. 种子数据（manage API，JWT）

| 步骤 | 命令/请求 | 预期 | 实测 |
|---|---|---|---|
| 登录 | `POST /api/manage/auth/login` | 200 + token | ✅ |
| 建项目 | `POST /api/manage/projects {key:"e2e",name:"E2E 联调项目"}` | 201 | ✅ id=1557510969872093184 |
| 建应用 | `POST /api/manage/projects/{id}/apps {appId:"com.e2e.sample",type:"console",platform:"windows"}` | 201 | ✅ id=1557511006182182912 |
| 建 ApiKey | `POST .../api-keys {scopes:["check","ingest","report"],appId:"com.e2e.sample"}` | 201 + 明文 key 一次性返回 | ✅ |

## 2. Packager 发布（local 存储 + HTTP 静态服务）

样例版本目录：`samples/AtomUpdate.Sample.Console` Release publish 产物复制两份，v1.1.0 增加 `new-feature.txt` 制造差异。

| 步骤 | 命令要点 | 预期 | 实测 |
|---|---|---|---|
| 发布 v1.0.0 | `publish --version 1.0.0 --source app-v1.0.0 --platform windows --arch x64 --project <pid> --app <aid> --api-url http://localhost:8080 --api-key <key> --credential-id e2e-local --publish --channels stable` | 全量 zip → 上传 → 推送 → 发布 stable | ✅ ASSET `http://localhost:8081/com.e2e.sample/1.0.0/e2e-com.e2e.sample-1.0.0-windows-x64.zip` |
| 发布 v1.1.0 | 同上 + `--from-version 1.0.0 --baseline app-v1.0.0` | 全量 + 增量双资产 | ✅ ASSET 全量 zip + `delta-1.0.0-to-1.1.0-x64.zip` |
| 资产可下载 | `curl` 两个资产 URL | 200 | ✅ full:200 delta:200 |

## 3. 示例宿主 check → download → 上报

`dotnet run --project samples/AtomUpdate.Sample.Console -c Release -- --server http://localhost:8080 --project-key e2e --app-id com.e2e.sample --version 1.0.0 --api-key <key>`（`ATOMUPDATE_REPORT__ENABLED=true` 打开上报）

| 验证点 | 预期 | 实测 |
|---|---|---|
| 检查 | Success=True HasUpdate=True | ✅ |
| 增量选中 | 1.0.0 → 1.1.0 选中 delta 资产 | ✅ 下载 URL 为 `delta-1.0.0-to-1.1.0-x64.zip`（520 B） |
| 下载 | 经 nginx HTTP 200，落 SDK 缓存目录 | ✅ |
| 状态机 | 落点 ReadyToInstall | ✅ |
| 上报 | `POST /api/stats/batch` → 200 | ✅ |
| 安装统计 | `GET /api/manage/stats/installations/summary` total=1、指纹去重=1、active24h=1 | ✅ |
| 下载统计 | `downloads/summary` downloads24h=1、successRate=1、deltaShare=1 | ✅ |

## 4. 304 / ETag 缓存命中

重复执行同一检查（版本未变）。

| 验证点 | 预期 | 实测 |
|---|---|---|
| 第二次检查 | 服务端 304（ETagHit=t），SDK 用缓存结论 | ✅ `checks/summary` eTagHitRate7d=0.5、policyUnchangedRate7d=0.5 |
| DB 证据 | `UpdateCheckLogs` 1 行 ETagHit=f + 1 行 ETagHit=t | ✅ |

## 5. 断网补报 + 幂等

| 步骤 | 预期 | 实测 |
|---|---|---|
| `docker compose stop web` 后跑宿主 | 检查失败重试；批量上报失败 → pending-reports 落盘 | ✅ `%LocalAppData%/AtomUpdate/state/com.e2e.sample/pending-reports/` 2 条 |
| `docker compose start web` 后重跑宿主 | 启动时补报 pending + 本次启动上报 | ✅ 日志"补报完成（3 条）"，pending 目录清空 |
| 服务端不双计 | Installations 仍 1 行（upsert 幂等） | ✅ total=1、distinctFingerprints=1 |
| `/api/report/install` 重放 | 同幂等键（AppId+InstallationId+ToVersion+Stage+ErrorCode）重复 POST → 200 duplicate=true、DB 单行 | ✅ 首次 `{accepted:true,duplicate:false}` → 重放 `{accepted:true,duplicate:true}`；`InstallationReports` 单行 |

## 6. Delta 落库证据

| 验证点 | 预期 | 实测 |
|---|---|---|
| `DownloadLogs.Kind` | delta 下载记 `Delta` | ✅ 3 行全部 `Kind=Delta, Success=t, ToVersion=1.1.0`（2 次在线下载 + 1 次补报） |

## 结论

M6 端到端链路全部通过：发布（Packager→local 存储→发布通道）→ 检查（200/304 双路径）→ 增量下载 → 统计上报（在线/离线补报）→ 服务端幂等 → 管理端统计四项指标（安装/指纹去重、下载成功率/delta 占比、304 命中率）全部与预期一致。

> 备注：演练用 ApiKey 为三 scope 合一以简化流程；生产建议 check / ingest / report 分 key（09-部署手册）。
