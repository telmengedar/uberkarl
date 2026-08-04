using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Covers the per-terrain default/fallback tile (DiVoid #7638, follow-up to Phase 3's terrain auto-tiling):
/// <see cref="TerrainDefinition.DefaultTile"/>'s model + JSON round-trip, backward compatibility with
/// pre-#7638 content, and <see cref="LevelLoader"/>'s resolution onto <see cref="ResolvedTerrain.DefaultTileId"/>
/// plus its typed validation (must name a declared tile that is itself a member of the SAME terrain).
/// Engine-agnostic throughout — the actual "Godot left this cell empty, fill it with the default" decision
/// is Godot-only state (<c>TileMapLayer.GetCellSourceId</c> after <c>SetCellsTerrainConnect</c>) and is
/// verified in-engine via Godot MCP (<c>TileMapLevelBuilder.ApplyDefaultTileToUnmatchedCells</c>,
/// <c>EditorCanvas.ApplyDefaultTile</c>) — see those types' doc comments and this project's established
/// convention for Godot-only rendering code (<c>AnimatedTileTests</c>'s doc comment).
/// </summary>
[TestFixture]
public sealed class TerrainDefaultTileTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath EarthInteriorPath = ResourcePath.Create("tiles/earth-interior.png");
    private static readonly ResourcePath EarthEdgePath = ResourcePath.Create("tiles/earth-edge.png");

    // ----- TerrainDefinition: default value -----

    [Test]
    public void TerrainDefinition_WithNoDefaultTile_IsNull()
    {
        var terrain = new TerrainDefinition { Id = 10, Name = "Earth" };

        Assert.That(terrain.DefaultTile, Is.Null);
    }

    // ----- Serialization round-trip -----

    [Test]
    public void TerrainDefaultTile_RoundTrips_ThroughSerializer()
    {
        var original = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(EarthInteriorPath), Terrain = 10, PeeringBits = TerrainPeering.All },
                new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(EarthEdgePath), Terrain = 10, PeeringBits = TerrainPeering.North },
            },
            TerrainSets = new[]
            {
                new TerrainSetDefinition
                {
                    Id = 1,
                    Name = "Ground",
                    MatchingMode = TerrainMatchMode.CornersAndSides,
                    Terrains = new[] { new TerrainDefinition { Id = 10, Name = "Earth", Color = "#8a5c34", DefaultTile = 1 } },
                },
            },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.That(restored.TerrainSets[0].Terrains[0].DefaultTile, Is.EqualTo(1));
    }

    [Test]
    public void TerrainWithNoDefaultTile_RoundTrips_AsNull()
    {
        var original = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(EarthInteriorPath), Terrain = 10 } },
            TerrainSets = new[]
            {
                new TerrainSetDefinition
                {
                    Id = 1,
                    Name = "Ground",
                    Terrains = new[] { new TerrainDefinition { Id = 10, Name = "Earth" } },
                },
            },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.That(restored.TerrainSets[0].Terrains[0].DefaultTile, Is.Null);
    }

    [Test]
    public void ReadTileSet_OnPreDefaultTileJson_WithNoDefaultTileKey_LoadsAsNull()
    {
        // Hand-authored to look exactly like content written before this PR — no "defaultTile" key on the
        // terrain (omit-when-default backward-compatibility bar, design #7580 §12).
        var json = """
            { "tiles": [ { "id": 1, "graphic": "self:tiles/earth-interior.png", "terrain": 10 } ],
              "terrainSets": [ { "id": 1, "name": "Ground", "terrains": [ { "id": 10, "name": "Earth" } ] } ] }
            """;

        var restored = LevelContentSerializer.ReadTileSet(Encoding.UTF8.GetBytes(json));

        Assert.That(restored.TerrainSets[0].Terrains[0].DefaultTile, Is.Null);
    }

    // ----- LevelLoader: resolution + validation -----

    [Test]
    public void Load_ResolvesDefaultTileId_OntoResolvedTerrain()
    {
        var tileSet = TwoVariantEarthTileSetWithDefault(defaultTileId: 1);
        var level = LevelWithTerrainChannel(new[] { LayerDefinition.EmptyCell }, new[] { 10 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.TerrainSets[0].Terrains[0].DefaultTileId, Is.EqualTo(1));
    }

    [Test]
    public void Load_WithNoDefaultTileDeclared_ResolvesDefaultTileIdAsNull()
    {
        var tileSet = TwoVariantEarthTileSetWithDefault(defaultTileId: null);
        var level = LevelWithTerrainChannel(new[] { LayerDefinition.EmptyCell }, new[] { 10 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.TerrainSets[0].Terrains[0].DefaultTileId, Is.Null);
    }

    [Test]
    public void Load_WhenDefaultTileReferencesUndeclaredTile_Throws()
    {
        var tileSet = TwoVariantEarthTileSetWithDefault(defaultTileId: 999);
        var level = LevelWithTerrainChannel(new[] { LayerDefinition.EmptyCell }, new[] { 10 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("default tile 999 is not a declared tile"));
    }

    [Test]
    public void Load_WhenDefaultTileBelongsToADifferentTerrain_Throws()
    {
        // Tile id 2 (the plain grass tile) exists but is not a member of the "Earth" terrain at all.
        var tileSet = TwoVariantEarthTileSetWithDefault(defaultTileId: 2);
        var level = LevelWithTerrainChannel(new[] { LayerDefinition.EmptyCell }, new[] { 10 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("does not belong to this terrain"));
    }

    private static TileSetDefinition TwoVariantEarthTileSetWithDefault(int? defaultTileId) => new()
    {
        Tiles = new[]
        {
            new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(EarthInteriorPath), Terrain = 10, PeeringBits = TerrainPeering.All },
            new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(GrassPath) },
        },
        TerrainSets = new[]
        {
            new TerrainSetDefinition
            {
                Id = 1,
                Name = "Ground",
                MatchingMode = TerrainMatchMode.CornersAndSides,
                Terrains = new[] { new TerrainDefinition { Id = 10, Name = "Earth", Color = "#8a5c34", DefaultTile = defaultTileId } },
            },
        },
    };

    private static LevelDefinition LevelWithTerrainChannel(int[] cells, int[] terrain) => new()
    {
        TileSize = 16,
        Width = cells.Length,
        Height = 1,
        TileSet = ResourceReference.ToSelf(TileSetPath),
        Layers = new[] { new LayerDefinition { Name = "ground", Cells = cells, Terrain = terrain } },
    };

    private static PackageBuilder BuildPackage(LevelDefinition level, TileSetDefinition tileSet)
    {
        var builder = new PackageBuilder().WithName("Demo Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.TileGraphic, EarthInteriorPath, Encoding.UTF8.GetBytes("EARTH-INTERIOR"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        return builder;
    }

    private static PackageRegistry OpenRegistry(PackageBuilder builder)
    {
        var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;
        return new PackageRegistry(PackageReader.Open(buffer));
    }
}
