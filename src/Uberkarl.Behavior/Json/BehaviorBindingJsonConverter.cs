namespace Uberkarl.Behavior.Json;

using System.Text.Json;
using System.Text.Json.Serialization;
using Uberkarl.Packages;

/// <summary>
/// Reads/writes <see cref="BehaviorBinding"/> as either <c>{ "script": "pkg:path" }</c> or
/// <c>{ "predefinedId": "id", "params": { ... } }</c> (design #7704 §5.2 — "either a <c>ResourceReference</c>
/// ... or a <c>{ predefinedId, params }</c> pair"). A full per-type converter is needed (mirrors
/// <c>Uberkarl.Content.Json.TileDefinitionJsonConverter</c>'s rationale) because <see cref="BehaviorBinding"/>
/// has no public constructor for reflection-based (de)serialization to use — it enforces "exactly one of
/// script/predefined, never both" by construction, which this converter must honor when reading. Public
/// (unlike the Content converters) because content packages beyond <c>Uberkarl.Content</c>'s own level/tileset
/// JSON reference bindings too (this core's own package resources), so <c>LevelContentSerializer</c> in
/// <c>Uberkarl.Content</c> registers this converter rather than <c>Uberkarl.Content</c> reimplementing it.
/// </summary>
public sealed class BehaviorBindingJsonConverter : JsonConverter<BehaviorBinding>
{
    public override BehaviorBinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (root.TryGetProperty("script", out var scriptElement) && scriptElement.ValueKind != JsonValueKind.Null)
        {
            var text = scriptElement.GetString();
            if (text is null)
                throw new JsonException("A behavior binding's 'script' must be a string.");
            try
            {
                return BehaviorBinding.FromScript(ResourceReference.Parse(text));
            }
            catch (FormatException exception)
            {
                throw new JsonException(exception.Message, exception);
            }
        }

        if (root.TryGetProperty("predefinedId", out var idElement) && idElement.ValueKind != JsonValueKind.Null)
        {
            var predefinedId = idElement.GetString();
            if (string.IsNullOrWhiteSpace(predefinedId))
                throw new JsonException("A behavior binding's 'predefinedId' must be a non-empty string.");

            IReadOnlyDictionary<string, object?>? parameters = null;
            if (root.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Object)
            {
                var values = new Dictionary<string, object?>();
                foreach (var property in paramsElement.EnumerateObject())
                    values[property.Name] = JsonScalarValue.Read(property.Value);
                parameters = values;
            }

            return BehaviorBinding.FromPredefined(predefinedId, parameters);
        }

        throw new JsonException("A behavior binding must declare either 'script' or 'predefinedId'.");
    }

    public override void Write(Utf8JsonWriter writer, BehaviorBinding value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.IsScript)
        {
            writer.WriteString("script", value.Script!.Value.ToString());
        }
        else
        {
            writer.WriteString("predefinedId", value.PredefinedId);
            if (value.Parameters.Count > 0)
            {
                writer.WritePropertyName("params");
                writer.WriteStartObject();
                foreach (var (key, val) in value.Parameters)
                {
                    writer.WritePropertyName(key);
                    JsonSerializer.Serialize(writer, val, options);
                }
                writer.WriteEndObject();
            }
        }
        writer.WriteEndObject();
    }
}
