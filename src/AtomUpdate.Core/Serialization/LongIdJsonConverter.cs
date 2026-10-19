using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtomUpdate.Core.Serialization;

/// <summary>IdGen 主键跨 JSON 边界以字符串输出（防 JS Number 精度丢失），读取兼容字符串与数字。</summary>
public sealed class LongIdJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => long.Parse(reader.GetString()!, CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetInt64(),
            _ => throw new JsonException("Id 必须是字符串或数字")
        };

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
