using System.Text.Json;
using System.Text.Json.Serialization;
using Uberkarl.Behavior;
using Uberkarl.Packages;

namespace Uberkarl.Content.Json;

/// <summary>
/// Reads/writes <see cref="TileDefinition"/>, migrating pre-Phase-4 content's legacy <c>"collides": bool</c>
/// field transparently into the new <see cref="TileDefinition.CollisionShape"/> descriptor (DiVoid #7551
/// Phase 4, design #7580 §12's omit-when-default backward-compatibility bar): <c>"collides":true</c>
/// becomes <see cref="CollisionShapeDefinition.Full"/>, <c>"collides":false</c> or an entirely absent
/// collision field becomes <see cref="CollisionShapeDefinition.None"/> — every tile set authored before
/// this phase loads unchanged. Freshly written content always uses the new <c>"collisionShape"</c> object
/// and never re-emits <c>"collides"</c> (the clean rename sweep — no lingering legacy writes). A full
/// per-type converter is needed (rather than a per-property one) because the legacy and new fields use
/// different JSON property names on the same object — reading must inspect the whole tile object to decide
/// which one is present.
/// </summary>
internal sealed class TileDefinitionJsonConverter : JsonConverter<TileDefinition>
{
    public override TileDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var id = root.GetProperty("id").GetInt32();
        var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind != JsonValueKind.Null
            ? nameElement.GetString()
            : null;
        var graphic = root.GetProperty("graphic").Deserialize<ResourceReference>(options);
        var frames = root.TryGetProperty("frames", out var framesElement)
            ? framesElement.Deserialize<ResourceReference[]>(options) ?? Array.Empty<ResourceReference>()
            : Array.Empty<ResourceReference>();
        var animationSpeed = root.TryGetProperty("animationSpeed", out var speedElement)
            ? speedElement.GetDouble()
            : TileDefinition.DefaultAnimationSpeed;
        var terrain = root.TryGetProperty("terrain", out var terrainElement) && terrainElement.ValueKind != JsonValueKind.Null
            ? terrainElement.GetInt32()
            : (int?)null;
        var peeringBits = root.TryGetProperty("peeringBits", out var peeringElement)
            ? (TerrainPeering)peeringElement.GetInt32()
            : TerrainPeering.None;
        var behavior = root.TryGetProperty("behavior", out var behaviorElement) && behaviorElement.ValueKind != JsonValueKind.Null
            ? behaviorElement.Deserialize<BehaviorBinding>(options)
            : null;

        return new TileDefinition
        {
            Id = id,
            Name = name,
            Graphic = graphic,
            CollisionShape = ReadCollisionShape(root, options),
            Frames = frames,
            AnimationSpeed = animationSpeed,
            Terrain = terrain,
            PeeringBits = peeringBits,
            Behavior = behavior,
        };
    }

    // Prefers the new descriptor when present; falls back to the legacy bool; defaults to
    // CollisionShapeDefinition.None when neither key is present (a tile authored with no collision opinion
    // at all — matches the old bool's own implicit default of false/no-collision).
    static CollisionShapeDefinition ReadCollisionShape(JsonElement root, JsonSerializerOptions options)
    {
        if (root.TryGetProperty("collisionShape", out var shapeElement) && shapeElement.ValueKind != JsonValueKind.Null)
            return shapeElement.Deserialize<CollisionShapeDefinition>(options) ?? CollisionShapeDefinition.None;

        if (root.TryGetProperty("collides", out var collidesElement) && collidesElement.ValueKind != JsonValueKind.Null)
            return collidesElement.GetBoolean() ? CollisionShapeDefinition.Full : CollisionShapeDefinition.None;

        return CollisionShapeDefinition.None;
    }

    public override void Write(Utf8JsonWriter writer, TileDefinition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", value.Id);
        if (value.Name is { } name)
            writer.WriteString("name", name);

        writer.WritePropertyName("graphic");
        JsonSerializer.Serialize(writer, value.Graphic, options);

        if (value.CollisionShape.Kind != CollisionShapeKind.None)
        {
            writer.WritePropertyName("collisionShape");
            JsonSerializer.Serialize(writer, value.CollisionShape, options);
        }

        if (value.Frames.Count > 0)
        {
            writer.WritePropertyName("frames");
            JsonSerializer.Serialize(writer, value.Frames, options);
        }

        writer.WriteNumber("animationSpeed", value.AnimationSpeed);

        if (value.Terrain is { } terrain)
            writer.WriteNumber("terrain", terrain);

        if (value.PeeringBits != TerrainPeering.None)
            writer.WriteNumber("peeringBits", (int)value.PeeringBits);

        if (value.Behavior is { } behavior)
        {
            writer.WritePropertyName("behavior");
            JsonSerializer.Serialize(writer, behavior, options);
        }

        writer.WriteEndObject();
    }
}
