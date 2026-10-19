using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AtomUpdate.Api.Data;

/// <summary>
/// SQLite 专用时间戳转换器：DateTimeOffset 以 UTC Ticks（long，INTEGER 列）存储。
/// SQLite provider 默认将 DateTimeOffset 映射为 TEXT 且无法翻译其 SQL 比较运算
/// （如日志清理的 WHERE CheckedAt &lt; cutoff、统计的 WHERE CheckedAt &gt;= since 均直接抛翻译异常），
/// 改为整数存储后比较/排序均为数值语义，且写入前统一归一为 UTC（docs/11 §3.3 时间戳 UTC 约定）。
/// </summary>
public sealed class SqliteDateTimeOffsetUtcTicksConverter : ValueConverter<DateTimeOffset, long>
{
    /// <summary>单例：可同时应用于 DateTimeOffset 与 DateTimeOffset? 属性
    /// （convertsNulls=false，null 不进入转换器、由 EF 在外层直通）。</summary>
    public static readonly SqliteDateTimeOffsetUtcTicksConverter Instance = new();

    private SqliteDateTimeOffsetUtcTicksConverter()
        : base(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero))
    {
    }
}
