using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AtomUpdate.Api.Data;

/// <summary>SQLite 连接打开即执行的 PRAGMA（docs/11 §3.4）：
/// journal_mode=WAL（读写并发：写不阻塞读）、synchronous=NORMAL（WAL 下安全且快）、
/// busy_timeout=5000（单写者约束下写锁等待上限 ms，超时即报错）、foreign_keys=ON（SQLite 默认关闭外键约束）。
/// busy_timeout/foreign_keys 为连接级参数，连接池复用后仍须逐次执行；WAL 持久于库文件，重复执行幂等。</summary>
public sealed class SqliteConnectionPragmasInterceptor : DbConnectionInterceptor
{
    public static readonly SqliteConnectionPragmasInterceptor Instance = new();

    private static readonly string[] Pragmas =
    [
        "PRAGMA journal_mode=WAL;",
        "PRAGMA synchronous=NORMAL;",
        "PRAGMA busy_timeout=5000;",
        "PRAGMA foreign_keys=ON;"
    ];

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Execute(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void Execute(DbConnection connection)
    {
        foreach (var pragma in Pragmas)
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            command.ExecuteNonQuery();
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var pragma in Pragmas)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
