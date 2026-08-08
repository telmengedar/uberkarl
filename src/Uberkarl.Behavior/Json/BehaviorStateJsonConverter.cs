namespace Uberkarl.Behavior.Json;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads/writes a state map (<see cref="Uberkarl.Content.ObjectDefinition.State"/>, an object placement's
/// initial state, etc.) as a plain JSON object of author-facing scalars — the same shape
/// <see cref="BehaviorBindingJsonConverter"/> already uses for a predefined binding's <c>params</c>. Needed
/// because the default reflection-based (de)serialization of <c>IReadOnlyDictionary&lt;string, object?&gt;</c>
/// would otherwise leave a raw <see cref="JsonElement"/> in every value.
/// </summary>
public sealed class BehaviorStateJsonConverter : JsonConverter<IReadOnlyDictionary<string, object?>>
{
    public override IReadOnlyDictionary<string, object?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var values = new Dictionary<string, object?>();
        foreach (var property in document.RootElement.EnumerateObject())
            values[property.Name] = JsonScalarValue.Read(property.Value);
        return values;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, val) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, val, options);
        }
        writer.WriteEndObject();
    }
}
