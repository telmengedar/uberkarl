using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uberkarl.Packages.Json;

internal sealed class ResourcePathJsonConverter : JsonConverter<ResourcePath>
{
    public override ResourcePath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null)
            throw new JsonException("Resource path must be a string.");
        return ResourcePath.Create(text);
    }

    public override void Write(Utf8JsonWriter writer, ResourcePath value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
