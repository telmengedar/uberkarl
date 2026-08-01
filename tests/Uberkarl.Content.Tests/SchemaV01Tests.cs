using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Covers the v0.1 schema additions: the tile <c>collides</c> flag, the layer <c>role</c>,
/// and the optional <c>playerStart</c>.
/// </summary>
[TestFixture]
public sealed class SchemaV01Tests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath SolidPath = ResourcePath.Create("tiles/solid.png");
    private static readonly ResourcePath DecorPath = ResourcePath.Create("tiles/decor.png");

    [Test]
    public void TileSet_CollidesFlag_RoundTrips()
    {
        var original = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(SolidPath), Collides = true },
                new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(DecorPath) },
            },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Tiles[0].Collides, Is.True);
            Assert.That(restored.Tiles[1].Collides, Is.False, "collides defaults to false when omitted");
        });
    }

    [Test]
    public void Layer_Role_SerializesAsCamelCaseStringAndRoundTrips()
    {
        var original = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "bg", Role = LayerRole.Background, Cells = new[] { LayerDefinition.EmptyCell } },
                new LayerDefinition { Name = "play", Role = LayerRole.Main, Cells = new[] { 1 } },
                new LayerDefinition { Name = "fg", Role = LayerRole.Foreground, Cells = new[] { 2 } },
            },
        };

        var json = Encoding.UTF8.GetString(LevelContentSerializer.WriteLevel(original));
        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(original));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"role\": \"main\""), "role emits camelCase string values");
            Assert.That(restored.Layers[0].Role, Is.EqualTo(LayerRole.Background));
            Assert.That(restored.Layers[1].Role, Is.EqualTo(LayerRole.Main));
            Assert.That(restored.Layers[2].Role, Is.EqualTo(LayerRole.Foreground));
        });
    }

    [Test]
    public void Layer_Role_DefaultsToBackground_WhenOmitted()
    {
        var json = Encoding.UTF8.GetBytes(
            "{\"tileSize\":16,\"width\":1,\"height\":1,\"tileSet\":\"self:tileset.json\"," +
            "\"layers\":[{\"name\":\"legacy\",\"cells\":[-1]}]}");

        var level = LevelContentSerializer.ReadLevel(json);

        Assert.That(level.Layers[0].Role, Is.EqualTo(LayerRole.Background));
    }

    [Test]
    public void PlayerStart_RoundTripsAndIsOmittedWhenNull()
    {
        var withStart = new LevelDefinition
        {
            TileSize = 16,
            Width = 4,
            Height = 4,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            PlayerStart = new GridPosition(2, 3),
        };
        var withoutStart = new LevelDefinition
        {
            TileSize = 16,
            Width = 4,
            Height = 4,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            PlayerStart = null,
        };

        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(withStart));
        var omittedJson = Encoding.UTF8.GetString(LevelContentSerializer.WriteLevel(withoutStart));

        Assert.Multiple(() =>
        {
            Assert.That(restored.PlayerStart, Is.EqualTo(new GridPosition(2, 3)));
            Assert.That(omittedJson, Does.Not.Contain("playerStart"));
        });
    }

    [Test]
    public void Load_CollectsCollidingTileIds_AndCarriesLayerRoles()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            PlayerStart = new GridPosition(0, 0),
            Layers = new[]
            {
                new LayerDefinition { Name = "bg", Role = LayerRole.Background, Cells = new[] { 2, LayerDefinition.EmptyCell } },
                new LayerDefinition { Name = "play", Role = LayerRole.Main, Cells = new[] { 1, 2 } },
            },
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.CollidingTileIds, Is.EquivalentTo(new[] { 1 }), "only tile 1 is flagged collides");
            Assert.That(resolved.Layers[0].Role, Is.EqualTo(LayerRole.Background));
            Assert.That(resolved.Layers[1].Role, Is.EqualTo(LayerRole.Main));
            Assert.That(resolved.PlayerStart, Is.EqualTo(new GridPosition(0, 0)));
        });
    }

    [Test]
    public void Load_WhenPlayerStartOutOfBounds_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 2,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            PlayerStart = new GridPosition(5, 0),
            Layers = new[] { new LayerDefinition { Name = "play", Role = LayerRole.Main, Cells = new[] { 1, 2, 1, 2 } } },
        };

        using var registry = BuildRegistry(level);

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("outside"));
    }

    private static PackageRegistry BuildRegistry(LevelDefinition level)
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(SolidPath), Collides = true },
                new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(DecorPath) },
            },
        };

        var builder = new PackageBuilder().WithName("Schema Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.TileGraphic, SolidPath, Encoding.UTF8.GetBytes("SOLID"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, DecorPath, Encoding.UTF8.GetBytes("DECOR"), "image/png");

        var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;
        return new PackageRegistry(PackageReader.Open(buffer));
    }
}
