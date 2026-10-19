using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtomUpdate.Core.Serialization;

public static class AtomUpdateJson
{
    public static JsonSerializerOptions Create() => ApplyTo(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions ApplyTo(JsonSerializerOptions options)
    {
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
