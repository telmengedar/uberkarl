using System.Text.Json;
using System.Text.Json.Serialization;
using Uberkarl.Packages;

namespace Uberkarl.Content.Json;

internal sealed class ResourceReferenceJsonConverter : JsonConverter<ResourceReference>
{
    public override ResourceReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null)
            throw new JsonException("Resource reference must be a string.");
        try
        {
            return ResourceReference.Parse(text);
        }
        catch (FormatException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, ResourceReference value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
