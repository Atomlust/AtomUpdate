using IdGen;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace AtomUpdate.Api.Data;

/// <summary>IdGen（Snowflake 64 位）应用侧主键生成；Add 时赋值，无需数据库往返。</summary>
public sealed class IdGenValueGenerator(IIdGenerator<long> generator) : ValueGenerator<long>
{
    public override bool GeneratesTemporaryValues => false;

    public override long Next(EntityEntry entry) => generator.CreateId();
}
