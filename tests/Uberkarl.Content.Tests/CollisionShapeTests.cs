using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Covers the collision-shape descriptor (DiVoid #7551 Phase 4, design #7580): the
/// <see cref="CollisionShapeDefinition"/> model itself, its JSON round-trip (including the migration of
/// pre-Phase-4 content's legacy <c>"collides": bool</c> field), <see cref="LevelLoader"/>'s validation, and
/// <see cref="CollisionShapeResolver"/>'s engine-agnostic preset/rect/polygon geometry — the part of the
/// Godot mapping (<c>TileSetBuilder.AddCollision</c>) that does not need Godot and so is unit-tested here
/// rather than only in-engine.
/// </summary>
[TestFixture]
public sealed class CollisionShapeTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");

    // ----- TileDefinition.CollisionShape default -----

    [Test]
    public void TileDefinition_DefaultsToNoCollisionShape()
    {
        var tile = new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) };

        Assert.That(tile.CollisionShape.Kind, Is.EqualTo(CollisionShapeKind.None));
    }

    // ----- JSON round-trip -----

    [Test]
    public void Full_RoundTrips_ThroughSerializer()
    {
        var original = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.That(restored.Tiles[0].CollisionShape.Kind, Is.EqualTo(CollisionShapeKind.Full));
    }

    [Test]
    public void None_IsOmittedFromJson_AndRoundTripsBackToNone()
    {
        var original = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) } },
        };

        var json = Encoding.UTF8.GetString(LevelContentSerializer.WriteTileSet(original));
        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("collisionShape"), "a None shape is the default and is omitted, matching design #7580 §8's omit-when-default contract");
            Assert.That(restored.Tiles[0].CollisionShape.Kind, Is.EqualTo(CollisionShapeKind.None));
        });
    }

    [Test]
    public void Rect_RoundTrips_ThroughSerializer()
    {
        var shape = CollisionShapeDefinition.FromRect(0.25f, 0.1f, 0.5f, 0.75f);
        var original = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = shape } },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));
        var restoredShape = restored.Tiles[0].CollisionShape;

        Assert.Multiple(() =>
        {
            Assert.That(restoredShape.Kind, Is.EqualTo(CollisionShapeKind.Rect));
            Assert.That(restoredShape.RectX, Is.EqualTo(0.25f));
            Assert.That(restoredShape.RectY, Is.EqualTo(0.1f));
            Assert.That(restoredShape.RectWidth, Is.EqualTo(0.5f));
            Assert.That(restoredShape.RectHeight, Is.EqualTo(0.75f));
        });
    }

    [Test]
    public void Polygon_RoundTrips_ThroughSerializer()
    {
        var points = new[]
        {
            new CollisionPointDefinition(0f, 0f),
            new CollisionPointDefinition(1f, 0.5f),
            new CollisionPointDefinition(0f, 1f),
        };
        var shape = CollisionShapeDefinition.FromPolygon(points);
        var original = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = shape } },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));
        var restoredShape = restored.Tiles[0].CollisionShape;

        Assert.Multiple(() =>
        {
            Assert.That(restoredShape.Kind, Is.EqualTo(CollisionShapeKind.Polygon));
            Assert.That(restoredShape.Points, Is.EqualTo(points));
        });
    }

    [TestCase(CollisionPreset.TopHalf)]
    [TestCase(CollisionPreset.BottomHalf)]
    [TestCase(CollisionPreset.LeftHalf)]
    [TestCase(CollisionPreset.RightHalf)]
    [TestCase(CollisionPreset.SlopeLeft)]
    [TestCase(CollisionPreset.SlopeRight)]
    public void Preset_RoundTrips_ThroughSerializer(CollisionPreset preset)
    {
        var original = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.FromPreset(preset) } },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));
        var restoredShape = restored.Tiles[0].CollisionShape;

        Assert.Multiple(() =>
        {
            Assert.That(restoredShape.Kind, Is.EqualTo(CollisionShapeKind.Preset));
            Assert.That(restoredShape.Preset, Is.EqualTo(preset));
        });
    }

    [Test]
    public void WrittenJson_NeverEmitsTheLegacyCollidesKey()
    {
        // The clean rename sweep (design #7580 Phase 4): freshly written content must never re-emit the
        // old "collides" key, even for a Full shape (which is exactly what "collides":true used to mean).
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.Full } },
        };

        var json = Encoding.UTF8.GetString(LevelContentSerializer.WriteTileSet(tileSet));

        Assert.That(json, Does.Not.Contain("\"collides\""));
    }

    // ----- Legacy "collides" bool migration -----

    [Test]
    public void ReadTileSet_OnLegacyCollidesTrue_MigratesToFull()
    {
        var json = """{ "tiles": [ { "id": 1, "graphic": "self:tiles/grass.png", "collides": true } ] }""";

        var restored = LevelContentSerializer.ReadTileSet(Encoding.UTF8.GetBytes(json));

        Assert.That(restored.Tiles[0].CollisionShape.Kind, Is.EqualTo(CollisionShapeKind.Full));
    }

    [Test]
    public void ReadTileSet_OnLegacyCollidesFalse_MigratesToNone()
    {
        var json = """{ "tiles": [ { "id": 1, "graphic": "self:tiles/grass.png", "collides": false } ] }""";

        var restored = LevelContentSerializer.ReadTileSet(Encoding.UTF8.GetBytes(json));

        Assert.That(restored.Tiles[0].CollisionShape.Kind, Is.EqualTo(CollisionShapeKind.None));
    }

    [Test]
    public void ReadTileSet_OnPrePhase1JsonWithNeitherField_LoadsAsNone()
    {
        // Hand-authored to look exactly like content written before even the original "collides" field
        // existed — no collision opinion at all.
        var json = """{ "tiles": [ { "id": 1, "graphic": "self:tiles/grass.png" } ] }""";

        var restored = LevelContentSerializer.ReadTileSet(Encoding.UTF8.GetBytes(json));

        Assert.That(restored.Tiles[0].CollisionShape.Kind, Is.EqualTo(CollisionShapeKind.None));
    }

    [Test]
    public void ReadTileSet_WhenBothCollidesAndCollisionShapePresent_PrefersTheNewField()
    {
        // Should never occur from any writer this codebase controls, but pins the documented precedence
        // (design: "prefer the new descriptor when present") in case of hand-edited/foreign content.
        var json = """{ "tiles": [ { "id": 1, "graphic": "self:tiles/grass.png", "collides": true, "collisionShape": { "kind": 0 } } ] }""";

        var restored = LevelContentSerializer.ReadTileSet(Encoding.UTF8.GetBytes(json));

        Assert.That(restored.Tiles[0].CollisionShape.Kind, Is.EqualTo(CollisionShapeKind.None), "kind 0 is None; the new field must win over the legacy bool");
    }

    // ----- LevelLoader validation -----

    [Test]
    public void Load_WhenRectShapeHasNonPositiveWidth_Throws()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = CollisionShapeDefinition.FromRect(0f, 0f, 0f, 1f) } },
        };

        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(BuildRegistry(tileSet), ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("non-positive size"));
    }

    [Test]
    public void Load_WhenPolygonShapeHasFewerThanThreePoints_Throws()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition
                {
                    Id = 1,
                    Graphic = ResourceReference.ToSelf(GrassPath),
                    CollisionShape = CollisionShapeDefinition.FromPolygon(new[] { new CollisionPointDefinition(0, 0), new CollisionPointDefinition(1, 1) }),
                },
            },
        };

        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(BuildRegistry(tileSet), ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("at least 3"));
    }

    [Test]
    public void Load_WhenPresetShapeNamesNoPreset_Throws()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = new CollisionShapeDefinition { Kind = CollisionShapeKind.Preset } } },
        };

        var exception = Assert.Throws<LevelContentException>(() => LevelLoader.Load(BuildRegistry(tileSet), ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("names no preset"));
    }

    [Test]
    public void Load_ResolvesTileCollisionShapesForEveryDeclaredTile_AndDerivesCollidingTileIds()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) }, // None
                new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(ResourcePath.Create("tiles/stone.png")), CollisionShape = CollisionShapeDefinition.Full },
                new TileDefinition { Id = 3, Graphic = ResourceReference.ToSelf(ResourcePath.Create("tiles/slope.png")), CollisionShape = CollisionShapeDefinition.FromPreset(CollisionPreset.SlopeLeft) },
            },
        };

        var resolved = LevelLoader.Load(BuildRegistry(tileSet), ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TileCollisionShapes[1].Kind, Is.EqualTo(CollisionShapeKind.None));
            Assert.That(resolved.TileCollisionShapes[2].Kind, Is.EqualTo(CollisionShapeKind.Full));
            Assert.That(resolved.TileCollisionShapes[3].Kind, Is.EqualTo(CollisionShapeKind.Preset));
            Assert.That(resolved.TileCollisionShapes[3].Preset, Is.EqualTo(CollisionPreset.SlopeLeft));
            Assert.That(resolved.CollidingTileIds, Is.EquivalentTo(new[] { 2, 3 }), "None (id 1) is not colliding; Full and Preset both are");
        });
    }

    // ----- CollisionShapeResolver: engine-agnostic geometry -----

    [Test]
    public void ResolvePoints_None_ReturnsNoPoints()
    {
        Assert.That(CollisionShapeResolver.ResolvePoints(CollisionShapeDefinition.None), Is.Empty);
    }

    [Test]
    public void ResolvePoints_Full_ReturnsTheUnitSquareCorners()
    {
        var points = CollisionShapeResolver.ResolvePoints(CollisionShapeDefinition.Full);

        Assert.That(points, Is.EqualTo(new[]
        {
            new CollisionPointDefinition(0f, 0f),
            new CollisionPointDefinition(1f, 0f),
            new CollisionPointDefinition(1f, 1f),
            new CollisionPointDefinition(0f, 1f),
        }));
    }

    [Test]
    public void ResolvePoints_Rect_ReturnsItsFourCorners()
    {
        var points = CollisionShapeResolver.ResolvePoints(CollisionShapeDefinition.FromRect(0.25f, 0.5f, 0.5f, 0.25f));

        Assert.That(points, Is.EqualTo(new[]
        {
            new CollisionPointDefinition(0.25f, 0.5f),
            new CollisionPointDefinition(0.75f, 0.5f),
            new CollisionPointDefinition(0.75f, 0.75f),
            new CollisionPointDefinition(0.25f, 0.75f),
        }));
    }

    [Test]
    public void ResolvePoints_Polygon_ReturnsThePointsVerbatim()
    {
        var points = new[] { new CollisionPointDefinition(0.1f, 0.2f), new CollisionPointDefinition(0.9f, 0.3f), new CollisionPointDefinition(0.5f, 0.9f) };

        Assert.That(CollisionShapeResolver.ResolvePoints(CollisionShapeDefinition.FromPolygon(points)), Is.EqualTo(points));
    }

    [TestCase(CollisionPreset.TopHalf, 0f, 0f, 1f, 0.5f)]
    [TestCase(CollisionPreset.BottomHalf, 0f, 0.5f, 1f, 0.5f)]
    [TestCase(CollisionPreset.LeftHalf, 0f, 0f, 0.5f, 1f)]
    [TestCase(CollisionPreset.RightHalf, 0.5f, 0f, 0.5f, 1f)]
    public void ResolvePoints_HalfTilePresets_ResolveToTheExpectedRect(CollisionPreset preset, float x, float y, float width, float height)
    {
        var points = CollisionShapeResolver.ResolvePoints(CollisionShapeDefinition.FromPreset(preset));

        Assert.That(points, Is.EqualTo(new[]
        {
            new CollisionPointDefinition(x, y),
            new CollisionPointDefinition(x + width, y),
            new CollisionPointDefinition(x + width, y + height),
            new CollisionPointDefinition(x, y + height),
        }));
    }

    [Test]
    public void ResolvePoints_SlopeLeft_HasItsHighPointOnTheLeftEdge()
    {
        var points = CollisionShapeResolver.ResolvePoints(CollisionShapeDefinition.FromPreset(CollisionPreset.SlopeLeft));

        Assert.Multiple(() =>
        {
            Assert.That(points, Has.Count.EqualTo(3), "a slope is a triangle");
            Assert.That(points, Does.Contain(new CollisionPointDefinition(0f, 0f)), "the top-left corner (the high point) is part of the shape");
            Assert.That(points, Does.Not.Contain(new CollisionPointDefinition(1f, 0f)), "the top-right corner is NOT part of the shape — the right side is low");
        });
    }

    [Test]
    public void ResolvePoints_SlopeRight_HasItsHighPointOnTheRightEdge()
    {
        var points = CollisionShapeResolver.ResolvePoints(CollisionShapeDefinition.FromPreset(CollisionPreset.SlopeRight));

        Assert.Multiple(() =>
        {
            Assert.That(points, Has.Count.EqualTo(3), "a slope is a triangle");
            Assert.That(points, Does.Contain(new CollisionPointDefinition(1f, 0f)), "the top-right corner (the high point) is part of the shape");
            Assert.That(points, Does.Not.Contain(new CollisionPointDefinition(0f, 0f)), "the top-left corner is NOT part of the shape — the left side is low");
        });
    }

    [Test]
    public void ResolvePoints_PresetShapeNamingNoPreset_Throws()
    {
        var shape = new CollisionShapeDefinition { Kind = CollisionShapeKind.Preset };

        Assert.Throws<LevelContentException>(() => CollisionShapeResolver.ResolvePoints(shape));
    }

    private static PackageRegistry BuildRegistry(TileSetDefinition tileSet)
    {
        var height = Math.Max(tileSet.Tiles.Count, 1);
        var cells = new int[height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var level = new LevelDefinition
        {
            TileSize = 16,
            Width = 1,
            Height = height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "ground", Cells = cells } },
        };

        var builder = new PackageBuilder().WithName("Collision Shape Test Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        foreach (var tile in tileSet.Tiles)
            builder.AddResource(ResourceKind.TileGraphic, tile.Graphic.Path, Encoding.UTF8.GetBytes($"PNG-{tile.Id}"), "image/png");

        var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;
        return new PackageRegistry(PackageReader.Open(buffer));
    }
}
