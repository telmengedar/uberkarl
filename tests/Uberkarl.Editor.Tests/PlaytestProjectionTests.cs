using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Editor;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the engine-agnostic half of playtest-from-editor (DiVoid #7514): the
/// <see cref="EditableLevelSnapshot"/> -&gt; <see cref="ResolvedLevel"/> projection carries everything the
/// play path (<c>TileMapLevelBuilder.Build</c>, collision, spawn) needs, and taking that projection never
/// mutates the editor's buffer — the "return to the editor with edits intact" guarantee. The Godot-side
/// launch/return glue (<c>PlaytestOverlay</c>, <c>LevelEditor.StartPlaytest/StopPlaytest</c>) is exercised
/// in-harness (see the design doc's verification log); this file pins the model-level contract it depends
/// on, independent of the engine.
/// </summary>
[TestFixture]
public sealed class PlaytestProjectionTests
{
    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 3;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath WaterPath = ResourcePath.Create("tiles/water.png");
    private static readonly ResourcePath DirtPath = ResourcePath.Create("tiles/dirt.png");

    // ----- projection carries everything the play path needs -----

    [Test]
    public void ToResolvedLevel_CarriesSpawnAndPerLayerPlayAttributes()
    {
        var level = EditableLevelReader.FromPackageBytes(BuildSamplePackageBytes());

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.DefaultSpawn, Is.EqualTo("start"));
            Assert.That(resolved.DefaultSpawnPosition, Is.EqualTo(new GridPosition(0, 0)));
            Assert.That(resolved.Layers[0].Collision, Is.True, "the terrain layer must collide for the player to stand on it.");
            Assert.That(resolved.Layers[0].ScrollSpeed, Is.EqualTo(1f));
            Assert.That(resolved.Layers[0].Repeat, Is.False);
        });
    }

    [Test]
    public void ToResolvedLevel_CarriesParallaxLayerAttributes_ForNonCollisionLayers()
    {
        // A background layer that scrolls slower than the world and repeats — exactly the parallax
        // backdrop the verification pass paints in before hitting Play.
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var terrain = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        var backdrop = new EditableLayer("backdrop", collision: false, scrollSpeed: 0.5f, repeat: true, (int[])cells.Clone());
        var level = new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new System.Collections.Generic.Dictionary<string, GridPosition>(), defaultSpawn: null,
            Palette(), new[] { terrain, backdrop });

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        var resolvedBackdrop = resolved.Layers[1];
        Assert.Multiple(() =>
        {
            Assert.That(resolvedBackdrop.Collision, Is.False, "a background layer must never collide, even if a placed tile 'collides'.");
            Assert.That(resolvedBackdrop.ScrollSpeed, Is.EqualTo(0.5f));
            Assert.That(resolvedBackdrop.Repeat, Is.True);
        });
    }

    [Test]
    public void ToResolvedLevel_WithNoDeclaredSpawn_HasNoDefaultSpawnPosition()
    {
        // A freshly-created (never-saved) level declares no spawn — the play path must fall back to a
        // default cell rather than fail. This is the projection half of that "empty level" edge case.
        var level = EditableLevel.CreateBlank("Untitled", TileSize, 5, 4, ResourceReference.ToSelf(TileSetPath), Palette());

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.DefaultSpawn, Is.Null);
            Assert.That(resolved.DefaultSpawnPosition, Is.Null);
            // CollidingTileIds reflects the PALETTE's declared solids (grass=1 collides:true), not what is
            // placed — the point here is the grid itself: nothing painted yet, every cell is the empty
            // marker, and that projects cleanly with no crash.
            Assert.That(resolved.Layers[0].Cells, Is.All.EqualTo(LayerDefinition.EmptyCell), "nothing painted yet — an entirely empty level projects cleanly.");
        });
    }

    // ----- animation carries through the live-preview/playtest projection (DiVoid #7551 Phase 2) -----
    // Regression coverage: this projection is the SAME one the editor canvas rebuilds from on every
    // TileSetModelChanged AND the one Play launches from (LevelEditor.OnTileSetModelChanged /
    // StartPlaytest both call EditableLevelSnapshot.ToResolvedLevel) — a caught-live bug had this
    // projection silently drop animation data (TileAnimations defaulted empty), so an author who just
    // added a second frame saw it render as simple in both the canvas AND playtest until the level was
    // reloaded from a saved package. Frame 0 = EditableTile.Graphic, matching LevelLoader's own contract.

    [Test]
    public void ToResolvedLevel_CarriesAnimationFrames_ForAnAnimatedTile()
    {
        var frame0 = Encoding.UTF8.GetBytes("GRASS-PNG");
        var frame1 = Encoding.UTF8.GetBytes("FRAME-2");
        var animatedGrass = new EditableTile(
            1, GrassPath, frame0, collides: true, name: null,
            frames: new[] { new EditableTileFrame(ResourcePath.Create("tiles/grass-2.png"), frame1) },
            animationSpeed: 12.0);
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var level = new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new System.Collections.Generic.Dictionary<string, GridPosition>(), defaultSpawn: null,
            new[] { animatedGrass }, new[] { new EditableLayer("terrain", true, 1f, false, cells) });

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TileAnimations, Contains.Key(1));
            var animation = resolved.TileAnimations[1];
            Assert.That(animation.Frames, Is.EqualTo(new[] { frame0, frame1 }));
            Assert.That(animation.Speed, Is.EqualTo(12.0));
        });
    }

    [Test]
    public void ToResolvedLevel_SimpleTile_HasNoAnimationEntry()
    {
        var level = SampleLevel();

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.That(resolved.TileAnimations, Is.Empty);
    }

    // ----- projecting for playtest never mutates the buffer (the "edits intact on return" guarantee) -----

    [Test]
    public void ToResolvedLevel_IsAPureProjection_LeavesSessionDirtyAndHistoryUntouched()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        var dirtyBefore = session.IsDirty;
        var canUndoBefore = session.CanUndo;
        var cellBefore = session.Level.GetCell(0, 0, 0);

        // Simulate hitting Play: project the buffer for the play runtime. Do it twice, as a launch
        // followed by a hypothetical re-launch would.
        var first = EditableLevelSnapshot.ToResolvedLevel(session.Level);
        var second = EditableLevelSnapshot.ToResolvedLevel(session.Level);

        Assert.Multiple(() =>
        {
            Assert.That(session.IsDirty, Is.EqualTo(dirtyBefore), "projecting for playtest must not touch the dirty flag.");
            Assert.That(session.CanUndo, Is.EqualTo(canUndoBefore), "projecting for playtest must not touch undo history.");
            Assert.That(session.Level.GetCell(0, 0, 0), Is.EqualTo(cellBefore), "projecting for playtest must not touch the model's cells.");
            Assert.That(first.Layers[0].Cells[0], Is.EqualTo(1));
            Assert.That(second.Layers[0].Cells[0], Is.EqualTo(1));
        });
    }

    [Test]
    public void AfterProjectingForPlaytest_TheSessionKeepsEditingNormally()
    {
        // Models "return to the editor": nothing about ending a playtest run calls Save(), reloads from
        // disk, or otherwise touches the session (see LevelEditor.StopPlaytest) — so from the model's
        // point of view a playtest launch is indistinguishable from "nothing happened", and editing must
        // continue exactly as if Play had never been pressed.
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        EditableLevelSnapshot.ToResolvedLevel(session.Level); // the playtest launch

        var change = session.PaintCell(0, 1, 0, 5);
        var undo = session.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(change, Is.EqualTo(new CellChange(0, 1, 0, 5)));
            Assert.That(session.Level.GetCell(0, 0, 0), Is.EqualTo(1), "the pre-playtest edit survived the round trip.");
            Assert.That(undo, Is.EqualTo(new CellChange(0, 1, 0, LayerDefinition.EmptyCell)));
        });
    }

    // ----- helpers -----

    private static IReadOnlyList<EditableTile> Palette() => new[]
    {
        new EditableTile(1, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), collides: true),
        new EditableTile(5, WaterPath, Encoding.UTF8.GetBytes("WATER-PNG"), collides: false),
    };

    private static EditableLevel SampleLevel()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        return new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new System.Collections.Generic.Dictionary<string, GridPosition>(), defaultSpawn: null,
            Palette(), new[] { layer });
    }

    // A real self-contained package with a declared default spawn — mirrors EditableLevelTests'
    // BuildSamplePackageBytes so both files exercise the same shape of real content.
    private static byte[] BuildSamplePackageBytes()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        cells[1 * Width + 1] = 1; // (1,1) grass, collides
        cells[1 * Width + 2] = 2; // (2,1) dirt, collides

        var tiles = new[]
        {
            new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), Collides = true },
            new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(DirtPath), Collides = true },
            new TileDefinition { Id = 5, Graphic = ResourceReference.ToSelf(WaterPath), Collides = false },
        };
        var tileSet = new TileSetDefinition { Tiles = tiles };

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Spawns = new System.Collections.Generic.Dictionary<string, GridPosition> { ["start"] = new GridPosition(0, 0) },
            DefaultSpawn = "start",
            Layers = new[]
            {
                new LayerDefinition { Name = "terrain", Collision = true, Cells = cells },
            },
        };

        var builder = new PackageBuilder().WithName("Demo Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, DirtPath, Encoding.UTF8.GetBytes("DIRT-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, WaterPath, Encoding.UTF8.GetBytes("WATER-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
