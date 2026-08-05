using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Editor.Input;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers level-grid resize (DiVoid #7550): the pure step arithmetic (<see cref="GridDimensionRules"/>),
/// the <see cref="EditableLevel"/> structural mutation (grow keeps cells / shrink crops, applied
/// identically across every layer) plus its confirm-gate query (<see cref="EditableLevel.WouldDropPaintedCells"/>),
/// the <see cref="LevelEditSession"/> resize intent and its history policy (a resize clears cell-edit
/// history — the same coordinate-aliasing hazard <see cref="EditHistory"/> already has for layer
/// delete/reorder, but for <c>(x,y)</c> rather than layer index), and the
/// <see cref="MenuOutcome.OpenResizePanel"/> routing seam. No Godot dependency.
/// </summary>
[TestFixture]
public sealed class LevelResizeTests
{
    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 3;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath WaterPath = ResourcePath.Create("tiles/water.png");

    // ----- GridDimensionRules -----

    [Test]
    public void Step_GrowsAndShrinksByOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GridDimensionRules.Step(10, +1), Is.EqualTo(11));
            Assert.That(GridDimensionRules.Step(10, -1), Is.EqualTo(9));
        });
    }

    [Test]
    public void Step_ClampsAtTheBottomEnd()
    {
        Assert.That(GridDimensionRules.Step(GridDimensionRules.MinDimension, -1), Is.EqualTo(GridDimensionRules.MinDimension));
    }

    [Test]
    public void Step_ClampsAtTheTopEnd()
    {
        Assert.That(GridDimensionRules.Step(GridDimensionRules.MaxDimension, +1), Is.EqualTo(GridDimensionRules.MaxDimension));
    }

    // ----- EditableLevel.WouldDropPaintedCells -----

    [Test]
    public void WouldDrop_GrowingBothDimensions_IsAlwaysFalse()
    {
        var level = PaintedLevel();
        Assert.That(level.WouldDropPaintedCells(Width + 5, Height + 5), Is.False);
    }

    [Test]
    public void WouldDrop_SameSize_IsFalse()
    {
        var level = PaintedLevel();
        Assert.That(level.WouldDropPaintedCells(Width, Height), Is.False);
    }

    [Test]
    public void WouldDrop_ShrinkingIntoOnlyEmptyMargin_IsFalse()
    {
        // Painted cell sits at (0,0); shrinking the far, untouched edge crops nothing painted.
        var level = PaintedLevel(paintX: 0, paintY: 0);
        Assert.That(level.WouldDropPaintedCells(Width - 1, Height), Is.False);
    }

    [Test]
    public void WouldDrop_ShrinkingWidthPastAPaintedColumn_IsTrue()
    {
        var level = PaintedLevel(paintX: Width - 1, paintY: 0);
        Assert.That(level.WouldDropPaintedCells(Width - 1, Height), Is.True);
    }

    [Test]
    public void WouldDrop_ShrinkingHeightPastAPaintedRow_IsTrue()
    {
        var level = PaintedLevel(paintX: 0, paintY: Height - 1);
        Assert.That(level.WouldDropPaintedCells(Width, Height - 1), Is.True);
    }

    [Test]
    public void WouldDrop_ChecksEveryLayer()
    {
        var level = SampleLevel();
        level.AppendLayer("Layer 2", collision: false, scrollSpeed: 1.0f, repeat: false);
        level.Layers[1].Cells[(Height - 1) * Width + (Width - 1)] = 1; // paint the new layer's far corner

        Assert.That(level.WouldDropPaintedCells(Width - 1, Height - 1), Is.True);
    }

    // ----- EditableLevel.Resize -----

    [Test]
    public void Resize_Grow_PreservesExistingCellsAtOriginalCoordsAndFillsNewCellsEmpty()
    {
        var level = PaintedLevel(paintX: 1, paintY: 1);

        var happened = level.Resize(Width + 2, Height + 2);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(level.Width, Is.EqualTo(Width + 2));
            Assert.That(level.Height, Is.EqualTo(Height + 2));
            Assert.That(level.GetCell(0, 1, 1), Is.EqualTo(1), "the original painted cell must survive at the same coordinate.");
            Assert.That(level.GetCell(0, Width, 0), Is.EqualTo(LayerDefinition.EmptyCell), "newly-added cells must start empty.");
            Assert.That(level.GetCell(0, 0, Height), Is.EqualTo(LayerDefinition.EmptyCell));
            Assert.That(level.Layers[0].Cells, Has.Length.EqualTo((Width + 2) * (Height + 2)));
        });
    }

    [Test]
    public void Resize_Shrink_CropsCellsOutsideNewBounds()
    {
        var level = PaintedLevel(paintX: 0, paintY: 0);
        level.Layers[0].Cells[(Height - 1) * Width + (Width - 1)] = 1; // also paint the far corner, which will be cropped

        var happened = level.Resize(Width - 1, Height - 1);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(level.Width, Is.EqualTo(Width - 1));
            Assert.That(level.Height, Is.EqualTo(Height - 1));
            Assert.That(level.GetCell(0, 0, 0), Is.EqualTo(1), "the surviving painted cell must still be there.");
            Assert.That(level.Layers[0].Cells, Has.Length.EqualTo((Width - 1) * (Height - 1)));
        });
    }

    [Test]
    public void Resize_AppliesConsistentlyAcrossEveryLayer()
    {
        var level = SampleLevel();
        level.AppendLayer("Layer 2", collision: false, scrollSpeed: 1.0f, repeat: false);
        level.Layers[0].Cells[0] = 1;
        level.Layers[1].Cells[0] = 1;

        level.Resize(Width + 1, Height + 1);

        Assert.Multiple(() =>
        {
            Assert.That(level.Layers[0].Cells, Has.Length.EqualTo((Width + 1) * (Height + 1)));
            Assert.That(level.Layers[1].Cells, Has.Length.EqualTo((Width + 1) * (Height + 1)));
            Assert.That(level.GetCell(0, 0, 0), Is.EqualTo(1));
            Assert.That(level.GetCell(1, 0, 0), Is.EqualTo(1));
        });
    }

    [Test]
    public void Resize_PreservesLayerNameCollisionScrollSpeedRepeat()
    {
        var level = SampleLevel(); // "terrain": collision:true, scrollSpeed:1, repeat:false

        level.Resize(Width + 1, Height);

        var layer = level.Layers[0];
        Assert.Multiple(() =>
        {
            Assert.That(layer.Name, Is.EqualTo("terrain"));
            Assert.That(layer.Collision, Is.True);
            Assert.That(layer.ScrollSpeed, Is.EqualTo(1.0f));
            Assert.That(layer.Repeat, Is.False);
        });
    }

    [Test]
    public void Resize_SameDimensions_IsNoOp()
    {
        var level = SampleLevel();
        var cellsBefore = level.Layers[0].Cells;

        var happened = level.Resize(Width, Height);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(level.Layers[0].Cells, Is.SameAs(cellsBefore), "a no-op resize must not reallocate anything.");
        });
    }

    [Test]
    public void Resize_KeepsTileSizeUnchanged()
    {
        var level = SampleLevel();
        level.Resize(Width * 2, Height * 2);
        Assert.That(level.TileSize, Is.EqualTo(TileSize));
    }

    [Test]
    public void Resize_NonPositiveDimensions_Throws()
    {
        var level = SampleLevel();
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => level.Resize(0, Height));
            Assert.Throws<ArgumentOutOfRangeException>(() => level.Resize(Width, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => level.Resize(-1, Height));
        });
    }

    // ----- LevelEditSession.Resize -----

    [Test]
    public void Session_Resize_ClearsCellHistory_MarksDirty()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True);

        var happened = session.Resize(Width + 1, Height);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(session.Level.Width, Is.EqualTo(Width + 1));
            Assert.That(session.CanUndo, Is.False, "resize changes the coordinate space — cell-edit history must clear.");
            Assert.That(session.IsDirty, Is.True);
        });
    }

    [Test]
    public void Session_Resize_NoOp_LeavesHistoryIntact_DoesNotMarkDirty()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True);

        var happened = session.Resize(Width, Height); // same size — a no-op

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(session.CanUndo, Is.True, "a no-op resize must not disturb history.");
        });
    }

    [Test]
    public void Session_Resize_NoOp_OnAFreshUndirtiedSession_StaysClean()
    {
        var session = new LevelEditSession(SampleLevel());

        var happened = session.Resize(Width, Height); // same size — a no-op

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_Resize_SaveLoadRoundTrip_PreservesNewDimensionsAndCells()
    {
        using var package = PackageReader.Open(new MemoryStream(BuildSamplePackageBytes()));
        var session = new LevelEditSession(EditableLevelReader.FromPackage(package));

        session.Resize(Width + 2, Height - 1); // mixed grow+shrink in one call

        var reloaded = EditableLevelReader.FromPackageBytes(session.Save(package));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Width, Is.EqualTo(Width + 2));
            Assert.That(reloaded.Height, Is.EqualTo(Height - 1));
            Assert.That(reloaded.Layers[0].Cells, Has.Length.EqualTo((Width + 2) * (Height - 1)));
        });
    }

    [Test]
    public void Session_Resize_SavedLevel_LoadsThroughTheRuntimeLoaderToo()
    {
        // The saved package must also be readable by the play-time LevelLoader (and therefore
        // PlayRuntimeBuilder's camera bounds, which read Width/Height straight off the resolved level).
        using var package = PackageReader.Open(new MemoryStream(BuildSamplePackageBytes()));
        var session = new LevelEditSession(EditableLevelReader.FromPackage(package));
        session.Resize(Width + 3, Height + 1);
        var savedBytes = session.Save(package);

        using var registry = new PackageRegistry(PackageReader.Open(new MemoryStream(savedBytes)));
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Width, Is.EqualTo(Width + 3));
            Assert.That(resolved.Height, Is.EqualTo(Height + 1));
        });
    }

    // ----- MenuOutcome.OpenResizePanel routing -----

    [Test]
    public void MenuOutcome_OpenResizePanel_CarriesTheRightKind()
    {
        var outcome = MenuOutcome.OpenResizePanel();
        Assert.That(outcome.Kind, Is.EqualTo(MenuOutcomeKind.OpenResizePanel));
    }

    [Test]
    public void RadialMenuModel_ResizeWedge_ResolvesToOpenResizePanel()
    {
        var menu = new RadialMenuModel("Actions", new[]
        {
            new RadialMenuItem("Undo", MenuOutcome.Invoke(EditorAction.Undo)),
            new RadialMenuItem("Resize…", MenuOutcome.OpenResizePanel()),
        });

        var resolved = menu.Resolve(0, 1); // wedge 1 of 2 (bottom)

        Assert.That(resolved.HasValue, Is.True);
        Assert.That(resolved!.Value.Kind, Is.EqualTo(MenuOutcomeKind.OpenResizePanel));
    }

    // ----- helpers -----

    private static IReadOnlyList<EditableTile> Palette() => new[]
    {
        new EditableTile(1, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.Full),
        new EditableTile(5, WaterPath, Encoding.UTF8.GetBytes("WATER-PNG"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None),
    };

    private static EditableLevel SampleLevel()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        return new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            Palette(), new[] { layer });
    }

    private static EditableLevel PaintedLevel(int paintX = 1, int paintY = 1)
    {
        var level = SampleLevel();
        level.Layers[0].Cells[paintY * Width + paintX] = 1;
        return level;
    }

    private static byte[] BuildSamplePackageBytes()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        cells[1 * Width + 1] = 1; // (1,1) grass

        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.Full } },
        };
        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[]
            {
                new LayerDefinition { Name = "terrain", Collision = true, Cells = cells },
            },
        };

        var builder = new PackageBuilder().WithName("Demo Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, Content.Json.LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.Level, LevelPath, Content.Json.LevelContentSerializer.WriteLevel(level));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
