using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uberkarl.Packages.Json;

internal static class ManifestSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static PackageManifest Read(Stream stream)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PackageManifest>(stream, Options);
            if (manifest is null)
                throw new PackageFormatException("Manifest could not be parsed.");
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new PackageFormatException("Manifest is not valid JSON.", exception);
        }
    }

    public static void Write(Stream stream, PackageManifest manifest)
    {
        JsonSerializer.Serialize(stream, manifest, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new PackageIdJsonConverter());
        options.Converters.Add(new ResourcePathJsonConverter());
        return options;
    }
}
