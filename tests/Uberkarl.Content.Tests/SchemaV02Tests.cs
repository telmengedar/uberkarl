using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Covers the v0.2 schema: the tile <c>collides</c> flag, the per-layer <c>collision</c> flag
/// (replacing the v0.1 role enum), and named <c>spawns</c> with a <c>defaultSpawn</c> (replacing
/// the v0.1 single <c>playerStart</c>).
/// </summary>
[TestFixture]
public sealed class SchemaV02Tests
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
    public void Layer_Collision_SerializesAsBoolAndRoundTrips()
    {
        var original = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, Cells = new[] { LayerDefinition.EmptyCell } },
                new LayerDefinition { Name = "terrain", Collision = true, Cells = new[] { 1 } },
            },
        };

        var json = Encoding.UTF8.GetString(LevelContentSerializer.WriteLevel(original));
        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(original));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"collision\": true"), "collision emits a boolean field");
            Assert.That(restored.Layers[0].Collision, Is.False);
            Assert.That(restored.Layers[1].Collision, Is.True);
        });
    }

    [Test]
    public void Layer_Collision_DefaultsToFalse_WhenOmitted()
    {
        var json = Encoding.UTF8.GetBytes(
            "{\"tileSize\":16,\"width\":1,\"height\":1,\"tileSet\":\"self:tileset.json\"," +
            "\"layers\":[{\"name\":\"legacy\",\"cells\":[-1]}]}");

        var level = LevelContentSerializer.ReadLevel(json);

        Assert.That(level.Layers[0].Collision, Is.False, "a layer is display-only unless it opts into collision");
    }

    [Test]
    public void Spawns_RoundTripAndDefaultSpawnSerializes()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 4,
            Height = 4,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Spawns = new Dictionary<string, GridPosition>
            {
                ["start"] = new GridPosition(1, 2),
                ["checkpoint"] = new GridPosition(3, 0),
            },
            DefaultSpawn = "start",
        };

        var json = Encoding.UTF8.GetString(LevelContentSerializer.WriteLevel(level));
        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(level));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"defaultSpawn\": \"start\""));
            Assert.That(restored.DefaultSpawn, Is.EqualTo("start"));
            Assert.That(restored.Spawns["start"], Is.EqualTo(new GridPosition(1, 2)));
            Assert.That(restored.Spawns["checkpoint"], Is.EqualTo(new GridPosition(3, 0)));
        });
    }

    [Test]
    public void Load_CarriesSpawnsLayerCollisionAndCollidingTiles()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Spawns = new Dictionary<string, GridPosition> { ["start"] = new GridPosition(0, 0) },
            DefaultSpawn = "start",
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, Cells = new[] { 2, LayerDefinition.EmptyCell } },
                new LayerDefinition { Name = "terrain", Collision = true, Cells = new[] { 1, 2 } },
            },
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.CollidingTileIds, Is.EquivalentTo(new[] { 1 }), "only tile 1 is flagged collides");
            Assert.That(resolved.Layers[0].Collision, Is.False);
            Assert.That(resolved.Layers[1].Collision, Is.True);
            Assert.That(resolved.DefaultSpawn, Is.EqualTo("start"));
            Assert.That(resolved.DefaultSpawnPosition, Is.EqualTo(new GridPosition(0, 0)));
        });
    }

    [Test]
    public void ResolvedLevel_TryGetSpawn_ResolvesNamedSpawn()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 4,
            Height = 4,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Spawns = new Dictionary<string, GridPosition>
            {
                ["start"] = new GridPosition(0, 0),
                ["door_b"] = new GridPosition(3, 3),
            },
            DefaultSpawn = "start",
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TryGetSpawn("door_b", out var cell), Is.True);
            Assert.That(cell, Is.EqualTo(new GridPosition(3, 3)));
            Assert.That(resolved.TryGetSpawn("missing", out _), Is.False);
        });
    }

    [Test]
    public void Load_WhenSpawnOutOfBounds_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 2,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Spawns = new Dictionary<string, GridPosition> { ["start"] = new GridPosition(5, 0) },
            DefaultSpawn = "start",
        };

        using var registry = BuildRegistry(level);

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("outside"));
    }

    [Test]
    public void Load_WhenDefaultSpawnMissingFromSpawns_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 2,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Spawns = new Dictionary<string, GridPosition> { ["start"] = new GridPosition(0, 0) },
            DefaultSpawn = "nonexistent",
        };

        using var registry = BuildRegistry(level);

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("Default spawn"));
    }

    [Test]
    public void Load_WhenSpawnsPresentButNoDefault_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 2,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Spawns = new Dictionary<string, GridPosition> { ["start"] = new GridPosition(0, 0) },
            DefaultSpawn = null,
        };

        using var registry = BuildRegistry(level);

        Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
    }

    [Test]
    public void Load_WhenNoSpawns_ResolvesWithNullDefaultSpawnPosition()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 2,
            TileSet = ResourceReference.ToSelf(TileSetPath),
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Spawns, Is.Empty);
            Assert.That(resolved.DefaultSpawn, Is.Null);
            Assert.That(resolved.DefaultSpawnPosition, Is.Null);
        });
    }

    [Test]
    public void Layer_ScrollSpeed_DefaultsToOne_WhenOmitted()
    {
        var json = Encoding.UTF8.GetBytes(
            "{\"tileSize\":16,\"width\":1,\"height\":1,\"tileSet\":\"self:tileset.json\"," +
            "\"layers\":[{\"name\":\"terrain\",\"collision\":true,\"cells\":[1]}]}");

        var level = LevelContentSerializer.ReadLevel(json);

        Assert.That(level.Layers[0].ScrollSpeed, Is.EqualTo(1f), "an omitted scrollSpeed loads as world-locked 1.0");
    }

    [Test]
    public void Layer_ScrollSpeed_RoundTrips()
    {
        var original = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, ScrollSpeed = 0.5f, Cells = new[] { LayerDefinition.EmptyCell } },
            },
        };

        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(original));

        Assert.That(restored.Layers[0].ScrollSpeed, Is.EqualTo(0.5f));
    }

    [Test]
    public void Load_CarriesScrollSpeedOntoResolvedLayer()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, ScrollSpeed = 0.5f, Cells = new[] { 2, LayerDefinition.EmptyCell } },
                new LayerDefinition { Name = "terrain", Collision = true, ScrollSpeed = 1f, Cells = new[] { 1, 2 } },
            },
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Layers[0].ScrollSpeed, Is.EqualTo(0.5f), "the parallax background carries its scroll speed");
            Assert.That(resolved.Layers[1].ScrollSpeed, Is.EqualTo(1f), "the world-locked terrain stays at 1.0");
        });
    }

    [Test]
    public void Load_WhenCollisionLayerHasNonUnitScrollSpeed_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "terrain", Collision = true, ScrollSpeed = 0.5f, Cells = new[] { 1, 2 } },
            },
        };

        using var registry = BuildRegistry(level);

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("scrollSpeed"));
        Assert.That(exception!.Message, Does.Contain("world-locked"));
    }

    [Test]
    public void Load_WhenNonCollisionLayerHasNonUnitScrollSpeed_Succeeds()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, ScrollSpeed = 1.5f, Cells = new[] { 2, LayerDefinition.EmptyCell } },
            },
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.Layers[0].ScrollSpeed, Is.EqualTo(1.5f), "a non-collision layer may take any scroll speed (foreground > 1.0 allowed)");
    }

    [Test]
    public void Level_BackgroundColor_SerializesAndRoundTrips()
    {
        var original = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            BackgroundColor = "#3A5A8C",
        };

        var json = Encoding.UTF8.GetString(LevelContentSerializer.WriteLevel(original));
        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(original));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"backgroundColor\": \"#3A5A8C\""));
            Assert.That(restored.BackgroundColor, Is.EqualTo("#3A5A8C"));
        });
    }

    [Test]
    public void Level_BackgroundColor_OmittedWhenNull()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
        };

        var json = Encoding.UTF8.GetString(LevelContentSerializer.WriteLevel(level));

        Assert.That(json, Does.Not.Contain("backgroundColor"), "an absent background fill is omitted from JSON");
    }

    [Test]
    public void Load_ParsesBackgroundColorToRgba()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            BackgroundColor = "#20408060",
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, Cells = new[] { LayerDefinition.EmptyCell } },
            },
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.BackgroundColor, Is.EqualTo(new RgbaColor(0x20, 0x40, 0x80, 0x60)));
    }

    [Test]
    public void Load_WhenNoBackgroundColor_ResolvesNull()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, Cells = new[] { LayerDefinition.EmptyCell } },
            },
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.BackgroundColor, Is.Null);
    }

    [Test]
    public void Load_WhenBackgroundColorMalformed_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            BackgroundColor = "not-a-colour",
        };

        using var registry = BuildRegistry(level);

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("hex colour"));
    }

    [Test]
    public void RgbaColor_TryParse_HandlesHexFormats()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RgbaColor.TryParse("#3A5A8C", out var withHash), Is.True);
            Assert.That(withHash, Is.EqualTo(new RgbaColor(0x3A, 0x5A, 0x8C, 255)), "6-digit hex is fully opaque");

            Assert.That(RgbaColor.TryParse("3a5a8c", out var noHashLower), Is.True);
            Assert.That(noHashLower, Is.EqualTo(new RgbaColor(0x3A, 0x5A, 0x8C, 255)), "leading # optional, case-insensitive");

            Assert.That(RgbaColor.TryParse("#20408060", out var withAlpha), Is.True);
            Assert.That(withAlpha, Is.EqualTo(new RgbaColor(0x20, 0x40, 0x80, 0x60)), "8-digit hex carries alpha");

            Assert.That(RgbaColor.TryParse("#FFF", out _), Is.False, "3-digit shorthand is not accepted");
            Assert.That(RgbaColor.TryParse("#GGGGGG", out _), Is.False, "non-hex digits are rejected");
            Assert.That(RgbaColor.TryParse("#204080GG", out _), Is.False, "a non-hex alpha pair is rejected");
            Assert.That(RgbaColor.TryParse("#12345", out _), Is.False, "a 5-digit length is rejected");
            Assert.That(RgbaColor.TryParse(null, out _), Is.False);
            Assert.That(RgbaColor.TryParse("", out _), Is.False);
        });
    }

    [Test]
    public void Layer_Repeat_DefaultsToFalse_WhenOmitted()
    {
        var json = Encoding.UTF8.GetBytes(
            "{\"tileSize\":16,\"width\":1,\"height\":1,\"tileSet\":\"self:tileset.json\"," +
            "\"layers\":[{\"name\":\"backdrop\",\"cells\":[-1]}]}");

        var level = LevelContentSerializer.ReadLevel(json);

        Assert.That(level.Layers[0].Repeat, Is.False, "a layer is finite unless it opts into repeat");
    }

    [Test]
    public void Layer_Repeat_SerializesAsBoolAndRoundTrips()
    {
        var original = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, ScrollSpeed = 0.5f, Repeat = true, Cells = new[] { LayerDefinition.EmptyCell } },
            },
        };

        var json = Encoding.UTF8.GetString(LevelContentSerializer.WriteLevel(original));
        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(original));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"repeat\": true"), "repeat emits a boolean field");
            Assert.That(restored.Layers[0].Repeat, Is.True);
        });
    }

    [Test]
    public void Load_CarriesRepeatOntoResolvedLayer()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, ScrollSpeed = 0.5f, Repeat = true, Cells = new[] { 2, LayerDefinition.EmptyCell } },
                new LayerDefinition { Name = "terrain", Collision = true, ScrollSpeed = 1f, Repeat = false, Cells = new[] { 1, 2 } },
            },
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Layers[0].Repeat, Is.True, "the repeating backdrop carries its repeat flag");
            Assert.That(resolved.Layers[1].Repeat, Is.False, "the finite terrain stays finite");
        });
    }

    [Test]
    public void Load_WhenCollisionLayerRepeats_Throws()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "terrain", Collision = true, ScrollSpeed = 1f, Repeat = true, Cells = new[] { 1, 2 } },
            },
        };

        using var registry = BuildRegistry(level);

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("repeat"));
            Assert.That(exception!.Message, Does.Contain("collision layer"));
        });
    }

    [Test]
    public void Load_WhenNonCollisionLayerRepeats_Succeeds()
    {
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 2,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "backdrop", Collision = false, ScrollSpeed = 0.5f, Repeat = true, Cells = new[] { 2, LayerDefinition.EmptyCell } },
            },
        };

        using var registry = BuildRegistry(level);
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.Layers[0].Repeat, Is.True, "a non-collision layer may repeat");
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
