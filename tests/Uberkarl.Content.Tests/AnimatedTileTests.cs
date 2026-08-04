using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content.Tests;

/// <summary>
/// Covers the animated-tile model (DiVoid #7551 Phase 2, design #7580): <see cref="TileDefinition"/>'s
/// structural simple/animated kind (<see cref="TileDefinition.IsAnimated"/> — no enum, frames present is
/// the whole rule), its JSON round-trip (<see cref="LevelContentSerializer"/>), backward compatibility
/// with pre-Phase-2 content, and <see cref="LevelLoader"/>'s resolution + validation of animation frames
/// and speed. Engine-agnostic throughout — no Godot; the Godot atlas-animation mapping
/// (<c>TileSetBuilder.BuildAnimatedSource</c>) is verified in-engine via Godot MCP per this project's
/// established convention for Godot-only rendering code (see <c>TileMapLevelBuilderTests</c>'s doc comment).
/// </summary>
[TestFixture]
public sealed class AnimatedTileTests
{
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath FrameTwoPath = ResourcePath.Create("tiles/grass-2.png");
    private static readonly ResourcePath FrameThreePath = ResourcePath.Create("tiles/grass-3.png");

    // ----- TileDefinition: structural kind -----

    [Test]
    public void TileDefinition_WithNoFrames_IsNotAnimated()
    {
        var tile = new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) };

        Assert.That(tile.IsAnimated, Is.False);
    }

    [Test]
    public void TileDefinition_WithAtLeastOneFrame_IsAnimated()
    {
        var tile = new TileDefinition
        {
            Id = 1,
            Graphic = ResourceReference.ToSelf(GrassPath),
            Frames = new[] { ResourceReference.ToSelf(FrameTwoPath) },
        };

        Assert.That(tile.IsAnimated, Is.True);
    }

    [Test]
    public void TileDefinition_DefaultAnimationSpeed_IsPositive()
    {
        // A hand-authored JSON sample that sets frames but omits speed must still resolve to something
        // playable rather than a zero/degenerate speed.
        var tile = new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) };

        Assert.That(tile.AnimationSpeed, Is.GreaterThan(0));
    }

    // ----- Serialization round-trip -----

    [Test]
    public void AnimatedTile_RoundTrips_FramesAndSpeed_ThroughSerializer()
    {
        var frame2 = ResourceReference.ToSelf(FrameTwoPath);
        var frame3 = ResourceReference.ToSelf(FrameThreePath);
        var original = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition
                {
                    Id = 1,
                    Graphic = ResourceReference.ToSelf(GrassPath),
                    Frames = new[] { frame2, frame3 },
                    AnimationSpeed = 12.0,
                },
            },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Tiles[0].IsAnimated, Is.True);
            Assert.That(restored.Tiles[0].Frames, Is.EqualTo(new[] { frame2, frame3 }));
            Assert.That(restored.Tiles[0].AnimationSpeed, Is.EqualTo(12.0));
        });
    }

    [Test]
    public void SimpleTile_RoundTrips_AsNotAnimated()
    {
        var original = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) } },
        };

        var restored = LevelContentSerializer.ReadTileSet(LevelContentSerializer.WriteTileSet(original));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Tiles[0].IsAnimated, Is.False);
            Assert.That(restored.Tiles[0].Frames, Is.Empty);
        });
    }

    [Test]
    public void ReadTileSet_OnPrePhase2Json_WithNoFramesOrSpeedFields_LoadsAsASimpleTile()
    {
        // Hand-authored to look exactly like content written before this PR (no "frames"/"animationSpeed"
        // keys at all) — the omit-when-default backward-compatibility bar (design #7580 §12).
        var json = """
            { "tiles": [ { "id": 1, "graphic": "self:tiles/grass.png", "collides": false } ] }
            """;

        var restored = LevelContentSerializer.ReadTileSet(Encoding.UTF8.GetBytes(json));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Tiles[0].IsAnimated, Is.False);
            Assert.That(restored.Tiles[0].AnimationSpeed, Is.GreaterThan(0));
        });
    }

    // ----- LevelLoader: resolution + validation -----

    [Test]
    public void Load_ResolvesAnimatedTile_AsFrame0PlusOrderedFrames_WithSpeed()
    {
        var frame0 = Encoding.UTF8.GetBytes("FRAME-0");
        var frame1 = Encoding.UTF8.GetBytes("FRAME-1");
        var frame2 = Encoding.UTF8.GetBytes("FRAME-2");

        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition
                {
                    Id = 1,
                    Graphic = ResourceReference.ToSelf(GrassPath),
                    Frames = new[] { ResourceReference.ToSelf(FrameTwoPath), ResourceReference.ToSelf(FrameThreePath) },
                    AnimationSpeed = 8.0,
                },
            },
        };
        var level = MinimalLevel(new[] { 1 });

        var builder = new PackageBuilder().WithName("Demo Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, frame0, "image/png");
        builder.AddResource(ResourceKind.TileGraphic, FrameTwoPath, frame1, "image/png");
        builder.AddResource(ResourceKind.TileGraphic, FrameThreePath, frame2, "image/png");
        using var registry = OpenRegistry(builder);

        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TileAnimations, Contains.Key(1));
            var animation = resolved.TileAnimations[1];
            Assert.That(animation.Frames, Is.EqualTo(new[] { frame0, frame1, frame2 }));
            Assert.That(animation.Speed, Is.EqualTo(8.0));
            // The primary graphic map still carries frame 0 — every existing single-graphic consumer
            // (thumbnails, the simple-tile path) keeps working unchanged.
            Assert.That(resolved.TileGraphics[1], Is.EqualTo(frame0));
        });
    }

    [Test]
    public void Load_SimpleTile_HasNoTileAnimationsEntry()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath) } },
        };
        var level = MinimalLevel(new[] { 1 });

        var builder = new PackageBuilder().WithName("Demo Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        using var registry = OpenRegistry(builder);

        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.TileAnimations, Does.Not.ContainKey(1));
    }

    [Test]
    public void Load_WhenAnimatedTileHasNonPositiveSpeed_Throws()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition
                {
                    Id = 1,
                    Graphic = ResourceReference.ToSelf(GrassPath),
                    Frames = new[] { ResourceReference.ToSelf(FrameTwoPath) },
                    AnimationSpeed = 0,
                },
            },
        };
        var level = MinimalLevel(new[] { 1 });

        var builder = new PackageBuilder().WithName("Demo Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, FrameTwoPath, Encoding.UTF8.GetBytes("FRAME-2"), "image/png");
        using var registry = OpenRegistry(builder);

        var exception = Assert.Throws<LevelContentException>(
            () => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
        Assert.That(exception!.Message, Does.Contain("animation speed"));
    }

    [Test]
    public void Load_WhenAnimationFrameGraphicMissing_Throws()
    {
        var tileSet = new TileSetDefinition
        {
            Tiles = new[]
            {
                new TileDefinition
                {
                    Id = 1,
                    Graphic = ResourceReference.ToSelf(GrassPath),
                    Frames = new[] { ResourceReference.ToSelf(ResourcePath.Create("tiles/missing-frame.png")) },
                },
            },
        };
        var level = MinimalLevel(new[] { 1 });

        var builder = new PackageBuilder().WithName("Demo Pack");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        using var registry = OpenRegistry(builder);

        Assert.Throws<LevelContentException>(() => LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath)));
    }

    private static LevelDefinition MinimalLevel(int[] cells) => new()
    {
        TileSize = 16,
        Width = cells.Length,
        Height = 1,
        TileSet = ResourceReference.ToSelf(TileSetPath),
        Layers = new[] { new LayerDefinition { Name = "ground", Cells = cells } },
    };

    private static PackageRegistry OpenRegistry(PackageBuilder builder)
    {
        var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;
        return new PackageRegistry(PackageReader.Open(buffer));
    }
}
