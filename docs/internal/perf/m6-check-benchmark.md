# M6 `/api/check` 压测报告

- 日期：2026-10-08
- 机器：ATOMLUST / 20C / 32GB，Windows
- 目标：`/api/check` P95 < 200ms（06 §3 M6 出口条件）
- 被测：AtomUpdate.Web Release（Development 配置）+ 本地 Docker `postgres:17-alpine`（`atomupdate` 库，StatsDomain 迁移已应用）
- 工具：`tools/perf/check-benchmark.ps1`（本机无 bombardier/k6，自动回退内置 HttpClient 并行循环；50 请求预热后统计 1000 请求，并发 20，Keep-Alive 连接复用）
- 种子：项目 `bench` / 应用 `com.bench.app`（无已发布版本，200 路径为无更新投影 + 策略体，304 路径为 ETag 重验证）

## 结果

| 模式 | 请求数 | 并发 | 错误 | RPS | P50 | P95 | P99 | 结论 |
|---|---|---|---|---|---|---|---|---|
| 200（完整响应） | 1000 | 20 | 0 | 251.7 | 9.10ms | **13.39ms** | 21.65ms | 通过 |
| 304（ETag 重验证） | 1000 | 20 | 0 | 263.8 | 7.84ms | **12.15ms** | 16.92ms | 通过 |

原始数据：`docs/perf/m6-200.json`、`docs/perf/m6-304.json`（同目录，由脚本 `-Output` 生成）。

## 结论

两条路径 P95 均 < 15ms，距 200ms 预算有 ~14 倍余量；与 M1 基线（P95≈26ms，本地手工）同量级。M6 性能出口条件达成。

复现：

```powershell
docker run -d --name atomupdate-pg-bench -p 5432:5432 -e POSTGRES_PASSWORD=123456789 -e POSTGRES_DB=atomupdate postgres:17-alpine
dotnet ef database update --project src\AtomUpdate.Api --startup-project src\AtomUpdate.Api
dotnet run --project src\AtomUpdate.Web -c Release
# 登录引导管理员 → 建项目/应用后：
./tools/perf/check-benchmark.ps1 -BaseUrl http://localhost:5188 -ProjectKey bench -AppId com.bench.app -Mode 200
./tools/perf/check-benchmark.ps1 -BaseUrl http://localhost:5188 -ProjectKey bench -AppId com.bench.app -Mode 304
```

注：安装 bombardier 或 k6 后脚本自动切换引擎（探测顺序 bombardier → k6 → 内置回退）。
