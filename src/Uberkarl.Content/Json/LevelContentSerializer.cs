using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uberkarl.Content.Json;

public static class LevelContentSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] WriteLevel(LevelDefinition level)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(level, Options));

    public static byte[] WriteTileSet(TileSetDefinition tileSet)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tileSet, Options));

    public static LevelDefinition ReadLevel(byte[] payload)
        => Deserialize<LevelDefinition>(payload, "level definition");

    public static TileSetDefinition ReadTileSet(byte[] payload)
        => Deserialize<TileSetDefinition>(payload, "tile set definition");

    private static T Deserialize<T>(byte[] payload, string description)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(payload, Options);
            if (value is null)
                throw new LevelContentException($"The {description} payload was empty.");
            return value;
        }
        catch (JsonException exception)
        {
            throw new LevelContentException($"The {description} payload is not valid JSON.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new ResourceReferenceJsonConverter());
        return options;
    }
}
