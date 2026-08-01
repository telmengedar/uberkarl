using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uberkarl.Packages.Json;

internal sealed class PackageIdJsonConverter : JsonConverter<PackageId>
{
    public override PackageId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null)
            throw new JsonException("Package id must be a string.");
        return PackageId.Parse(text);
    }

    public override void Write(Utf8JsonWriter writer, PackageId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
