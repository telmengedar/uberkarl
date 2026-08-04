using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Covers the terrain/meta-tile (auto-tiling) model (DiVoid #7551 Phase 3, design #7580): the
/// engine-agnostic <see cref="TerrainSetDefinition"/>/<see cref="TerrainDefinition"/> schema, a tile's
/// terrain membership + peering bits, the layer's parallel logical terrain-paint channel
/// (<see cref="LayerDefinition.Terrain"/>), its JSON round-trip, backward compatibility with pre-Phase-3
/// content, and <see cref="LevelLoader"/>'s resolution + validation (declared-terrain references, the
/// two-channel concrete-XOR-terrain invariant, terrain-channel length). Engine-agnostic throughout — no
/// Godot; the Godot terrain-set/peering-bit mapping (<c>TileSetBuilder</c>) and the
/// <c>TileMapLayer.SetCellsTerrainConnect</c> resolution (<c>TileMapLevelBuilder.ConnectTerrain</c>) are
/// verified in-engine via Godot MCP per this project's established convention for Godot-only rendering
/// code (see <c>AnimatedTileTests</c>'s doc comment).
/// </summary>
[TestFixture]
public sealed class TerrainTileTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath EarthInteriorPath = ResourcePath.Create("tiles/earth-interior.png");
    private static readonly ResourcePath EarthEdgePath = ResourcePath.Create("tiles/earth-edge.png");

    // ----- TileDefinition: terrain membership defaults -----

    [Test]
    public void TileDefinition_WithNoTerrain_IsNull()
    {
        var tile = new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) };

        Assert.Multiple(() =>
        {
            Assert.That(tile.Terrain, Is.Null);
            Assert.That(tile.PeeringBits, Is.EqualTo(TerrainPeering.None));
        });
    }

    // ----- Serialization round-trip -----

    [Test]
    public void TerrainSet_RoundTrips_ThroughSerializer()
    {
        var original = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition
                {
                    Id = 1,
                    Graphic = ResourceReference.ToSelf(EarthInteriorPath),
                    Terrain = 10,
                    PeeringBits = TerrainPeering.All,
                },
                new TileDefinition
                {
                    Id = 2,
                    Graphic = ResourceReference.ToSelf(EarthEdgePath),
                    Terrain = 10,
                    PeeringBits = TerrainPeering.All & ~TerrainPeering.North,
                },
            },
            TerrainSets = new[]
            {
                new TerrainSetDefinition
                {
                    Id = 1,
                    Name = "Ground",
                    MatchingMode = TerrainMatchMode.CornersAndSides,
                    Terrains = new[] { new TerrainDefinition { Id = 10, Name = "Earth", Color = "#8a5c34" } },
                },
            },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.Multiple(() =>
        {
            Assert.That(restored.TerrainSets, Has.Count.EqualTo(1));
            Assert.That(restored.TerrainSets[0].Id, Is.EqualTo(1));
            Assert.That(restored.TerrainSets[0].Name, Is.EqualTo("Ground"));
            Assert.That(restored.TerrainSets[0].MatchingMode, Is.EqualTo(TerrainMatchMode.CornersAndSides));
            Assert.That(restored.TerrainSets[0].Terrains, Has.Count.EqualTo(1));
            Assert.That(restored.TerrainSets[0].Terrains[0].Id, Is.EqualTo(10));
            Assert.That(restored.TerrainSets[0].Terrains[0].Name, Is.EqualTo("Earth"));
            Assert.That(restored.TerrainSets[0].Terrains[0].Color, Is.EqualTo("#8a5c34"));
            Assert.That(restored.Tiles[0].Terrain, Is.EqualTo(10));
            Assert.That(restored.Tiles[0].PeeringBits, Is.EqualTo(TerrainPeering.All));
            Assert.That(restored.Tiles[1].PeeringBits, Is.EqualTo(TerrainPeering.All & ~TerrainPeering.North));
        });
    }

    [Test]
    public void PlainTileSet_RoundTrips_WithNoTerrainSets()
    {
        var original = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) } },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.That(restored.TerrainSets, Is.Empty);
    }

    [Test]
    public void ReadTileSet_OnPrePhase3Json_WithNoTerrainFields_LoadsAsAPlainTile()
    {
        // Hand-authored to look exactly like content written before this PR — no "terrainSets" key at the
        // tile-set level, no "terrain"/"peeringBits" keys on the tile (design #7580 §12's omit-when-default
        // backward-compatibility bar).
        var json = """
            { "tiles": [ { "id": 1, "graphic": "self:tiles/grass.png", "collides": false } ] }
            """;

        var restored = LevelContentSerializer.ReadTileSet(Encoding.UTF8.GetBytes(json));

        Assert.Multiple(() =>
        {
            Assert.That(restored.TerrainSets, Is.Empty);
            Assert.That(restored.Tiles[0].Terrain, Is.Null);
        });
    }

    [Test]
    public void Layer_Terrain_RoundTrips_ThroughSerializer()
    {
        var original = new LevelDefinition
        {
            TileSize = 16,
            Width = 3,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition
                {
                    Name = "ground",
                    Cells = new[] { LayerDefinition.EmptyCell, 1, LayerDefinition.EmptyCell },
                    Terrain = new[] { 10, LayerDefinition.EmptyCell, 10 },
                },
            },
        };

        var restored = LevelContentSerializer.ReadLevel(LevelContentSerializer.WriteLevel(original));

        Assert.That(restored.Layers[0].Terrain, Is.EqualTo(new[] { 10, LayerDefinition.EmptyCell, 10 }));
    }

    [Test]
    public void ReadLevel_OnPrePhase3Json_WithNoTerrainChannel_LoadsWithEmptyTerrain()
    {
        var json = """
            { "tileSize": 16, "width": 1, "height": 1, "tileSet": "self:tileset.json",
              "layers": [ { "name": "ground", "cells": [1] } ] }
            """;

        var restored = LevelContentSerializer.ReadLevel(Encoding.UTF8.GetBytes(json));

        Assert.That(restored.Layers[0].Terrain, Is.Empty);
    }

    // ----- LevelLoader: resolution -----

    [Test]
    public void Load_ResolvesTerrainSetsAndTileTerrains()
    {
        var tileSet = TwoVariantEarthTileSet();
        var level = LevelWithTerrainChannel(new[] { LayerDefinition.EmptyCell }, new[] { 10 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TerrainSets, Has.Count.EqualTo(1));
            Assert.That(resolved.TerrainSets[0].Terrains[0].Id, Is.EqualTo(10));
            Assert.That(resolved.TerrainSets[0].Terrains[0].Color, Is.EqualTo(new RgbaColor(0x8a, 0x5c, 0x34, 255)));
            Assert.That(resolved.TileTerrains, Contains.Key(1));
            Assert.That(resolved.TileTerrains[1].TerrainId, Is.EqualTo(10));
            Assert.That(resolved.TileTerrains[1].TerrainSetId, Is.EqualTo(1));
            Assert.That(resolved.TileTerrains[1].PeeringBits, Is.EqualTo(TerrainPeering.All));
            Assert.That(resolved.TileTerrains, Does.Not.ContainKey(2), "the plain grass tile is not a terrain member.");
        });
    }

    [Test]
    public void Load_ResolvedLayerTerrain_IsAlwaysFullyPopulated_EvenWhenLayerDeclaresNone()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) } },
        };
        var level = MinimalLevel(new[] { 1, 1, 1 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        // ResolvedLayer.Terrain is always fully populated (unlike LayerDefinition.Terrain, which may be
        // empty) so TileMapLevelBuilder can always index it in lockstep with Cells.
        Assert.That(resolved.Layers[0].Terrain, Is.EqualTo(new[] { LayerDefinition.EmptyCell, LayerDefinition.EmptyCell, LayerDefinition.EmptyCell }));
    }

    [Test]
    public void Load_WhenTileTerrainReferencesUndeclaredTerrain_Throws()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), Terrain = 999 } },
        };
        var level = MinimalLevel(new[] { 1 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("undeclared terrain"));
    }

    [Test]
    public void Load_WhenDuplicateTerrainIdAcrossSets_Throws()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) } },
            TerrainSets = new[]
            {
                new TerrainSetDefinition { Id = 1, Name = "A", Terrains = new[] { new TerrainDefinition { Id = 10, Name = "Earth" } } },
                new TerrainSetDefinition { Id = 2, Name = "B", Terrains = new[] { new TerrainDefinition { Id = 10, Name = "Sand" } } },
            },
        };
        var level = MinimalLevel(new[] { 1 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("Terrain id 10"));
    }

    [Test]
    public void Load_WhenLayerTerrainLengthMismatchesGrid_Throws()
    {
        var tileSet = TwoVariantEarthTileSet();
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 3,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition
                {
                    Name = "ground",
                    Cells = new[] { LayerDefinition.EmptyCell, LayerDefinition.EmptyCell, LayerDefinition.EmptyCell },
                    Terrain = new[] { 10, 10 }, // 2 entries, grid needs 3
                },
            },
        };

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("terrain cells"));
    }

    [Test]
    public void Load_WhenLayerTerrainReferencesUndeclaredTerrain_Throws()
    {
        var tileSet = TwoVariantEarthTileSet();
        var level = LevelWithTerrainChannel(new[] { LayerDefinition.EmptyCell }, new[] { 999 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("undefined terrain id"));
    }

    // The two-channel invariant (design #7580 §7): a cell must never be BOTH a concrete tile AND
    // terrain-painted at once.
    [Test]
    public void Load_WhenCellIsBothConcreteAndTerrainPainted_Throws()
    {
        var tileSet = TwoVariantEarthTileSet();
        var level = LevelWithTerrainChannel(new[] { 2 }, new[] { 10 }); // cell 0: concrete tile 2 AND terrain 10

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("both a concrete tile"));
    }

    [Test]
    public void Load_TerrainPaintedCell_LeavesCellsEntryEmpty_NotFlaggedAsMissingTile()
    {
        // A terrain-painted cell's Cells entry is EmptyCell — it must not trip the ordinary
        // "cell references an undefined tile id" validation (that check is skipped for EmptyCell already;
        // this pins that terrain painting doesn't regress it).
        var tileSet = TwoVariantEarthTileSet();
        var level = LevelWithTerrainChannel(new[] { LayerDefinition.EmptyCell }, new[] { 10 });

        using var registry = OpenRegistry(BuildPackage(level, tileSet));

        Assert.DoesNotThrow(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
    }

    private static TileSetDefinition TwoVariantEarthTileSet() => new()
    {
        Tiles = new[]
        {
            new TileDefinition
            {
                Id = 1,
                Graphic = ResourceReference.ToSelf(EarthInteriorPath),
                Terrain = 10,
                PeeringBits = TerrainPeering.All,
            },
            new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(GrassPath) },
        },
        TerrainSets = new[]
        {
            new TerrainSetDefinition
            {
                Id = 1,
                Name = "Ground",
                MatchingMode = TerrainMatchMode.CornersAndSides,
                Terrains = new[] { new TerrainDefinition { Id = 10, Name = "Earth", Color = "#8a5c34" } },
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

    private static LevelDefinition MinimalLevel(int[] cells) => new()
    {
        TileSize = 16,
        Width = cells.Length,
        Height = 1,
        TileSet = ResourceReference.ToSelf(TileSetPath),
        Layers = new[] { new LayerDefinition { Name = "ground", Cells = cells } },
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
