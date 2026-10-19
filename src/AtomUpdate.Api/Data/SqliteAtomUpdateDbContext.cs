using IdGen;
using Microsoft.EntityFrameworkCore;

namespace AtomUpdate.Api.Data;

/// <summary>SQLite provider 专用上下文（docs/11 §3.2 迁移隔离方案）：EF 迁移发现以「上下文类型」过滤
/// （[DbContext] 标注），两套 provider 专有迁移共用同一程序集时，以派生上下文承载 SQLite 迁移集
/// （Data/Migrations/Sqlite，命名空间 AtomUpdate.Api.Data.SqliteMigrations），避免与 Npgsql 迁移被同时发现。
/// 模型与基类完全一致（含 provider 专有的 SQLite 值转换分支）；运行时仅在 sqlite 模式注册，
/// 专用于启动迁移与 dotnet-ef 设计时（--context SqliteAtomUpdateDbContext），常规读写仍走 AtomUpdateDbContext。</summary>
public sealed class SqliteAtomUpdateDbContext(
    DbContextOptions<SqliteAtomUpdateDbContext> options,
    TenantContext tenantContext,
    IIdGenerator<long> idGenerator)
    : AtomUpdateDbContext(options, tenantContext, idGenerator);
