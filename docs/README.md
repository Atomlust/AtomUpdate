# AtomUpdate 项目文档

AtomUpdate 是一套面向 .NET 11 应用（兼顾其他语言客户端）的在线更新平台：服务端统一存储与分发更新包，打包工具完成"打包 → 上传 → 发布"全链路，客户端 SDK 与独立更新器负责检查、下载、校验、安装与回滚。支持私有化部署（Docker Compose 一键启动），服务端无状态可水平扩展。

## 核心特性

- **服务端一体**：ASP.NET Core 11 单进程托管更新 API 与 Blazor 管理后台，默认 SQLite 零配置、生产可切 PostgreSQL，更新包落盘 `Storage:Root` 并经 `/uploads` 静态分发（支持 Range 断点续传）。
- **响应分层 + ETag/304**：无更新返回精简响应，策略用 `policyVersion` 增量下发，绝大多数请求为 O(1) 级小响应。
- **多通道发布**：dev / preview / beta / rc / stable 五条发布轨道，版本 × 通道发布矩阵，支持强制更新与最低支持版本。
- **文件级增量包**：打包工具生成 delta 包（files + delete 清单），SDK 优先选择增量，更新器应用删除清单。
- **跨平台客户端**：SDK 支持 net11.0（WPF/WinForms/Avalonia/MAUI/控制台/服务型宿主），更新器为 Native AOT 单文件（6 RID 内嵌 NuGet），提权、备份、回滚、钩子、双进程自升级全链路。
- **公开检查端点**：`/api/check` 与上报端点公开（无需密钥），发布/管理操作走邮箱/密码登录 JWT。
- **完整可观测**：安装统计、下载/检查日志、审计日志、健康探针。

## 快速开始

### 1. 部署服务端（Docker Compose，2 步）

```powershell
# 仓库根目录启动（默认 SQLite 轻量单容器，零必填配置）
docker compose up -d --build
curl http://localhost:8081/health   # {"status":"ok"}
```

首次访问 `http://localhost:8081` 进入**设置向导**创建平台管理员即可（JWT 密钥自动生成并持久化）。生产 PostgreSQL 形态、环境变量、备份、CI/发布流水线详见 [09 部署手册](09-部署手册.md)。

### 2. 发布一个版本（打包工具，双击即用）

运行 `AtomUpdate.Packager.exe`（Avalonia GUI，`dist/` 产物），四步向导：目标（登录 + 选项目/应用与发布目录）→ 打包 → 更新日志 → 发布。也可用 CLI（dotnet tool `atomupdate-packager`，含 publish/pack/upload/list 四个子命令）集成 CI/CD。详见 [05 打包工具与存储设计](05-打包工具与存储设计.md)。

### 3. 客户端接入（一段代码）

```powershell
dotnet add package AtomUpdate   # net11.0，自包含标准更新器
```

```csharp
builder.Services.AddAtomUpdate(options =>
{
    options.ServerUrl = "https://update.example.com";
    options.ProjectKey = "myproj";
    options.AppId = "com.example.myapp";
    options.CurrentVersion = "1.0.0";
});
// 之后：updater.CheckAndDownloadAsync(...) 检查下载，updater.InstallAndRestartAsync() 安装重启
```

详见 [08 SDK 接入指南](08-SDK接入指南.md)（含 WPF/MAUI/Java 等三场景示例）。

## 文档索引

| 文档 | 内容 |
|---|---|
| [01 需求分析](01-需求分析.md) | 产品定位、核心功能列表、术语表 |
| [02 总体架构设计](02-总体架构设计.md) | 系统全景、模块划分与依赖、关键流程、技术选型、安全设计、部署形态 |
| [03 数据模型与 API 设计](03-数据模型与API设计.md) | 实体表结构、更新检查 API 契约、ETag 实现细节、API 端点清单（与 [contracts/openapi.v1.json](contracts/openapi.v1.json) 一致） |
| [04 客户端 SDK 与更新器设计](04-客户端SDK与更新器设计.md) | SDK 包结构/配置/事件/状态机、更新器流程/提权/回滚/自升级、SDK↔更新器协议 |
| [05 打包工具与存储设计](05-打包工具与存储设计.md) | 打包工具使用说明、包格式与增量包、上传与登录、CLI、存储布局 |
| [08 SDK 接入指南](08-SDK接入指南.md) | 最小接入、全部配置键、IAtomUpdater 成员与事件、三场景示例（桌面/MAUI/REST 直连） |
| [09 部署手册](09-部署手册.md) | Docker Compose 部署、环境变量、数据备份、已知限制 |
| [10 更新器配置参考](10-更新器配置参考.md) | 配置五层、全部配置键、命令行参数、退出码、状态文件协议、平台目录 |
| [11 数据库选型与部署指南](11-数据库选型与部署指南.md) | PostgreSQL/SQLite 决策矩阵、双 provider 数据访问实现（类型映射/WAL/迁移集/写路径参数） |
| [API 契约](contracts/openapi.v1.json) | OpenAPI v1 完整契约（由 Api 服务 `/openapi/v1.json` 导出，端点清单见 03 §3.4） |
