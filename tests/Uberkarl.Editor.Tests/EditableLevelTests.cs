using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Editor;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the engine-agnostic editor core: applying an edit, the undo/redo command seam, and the
/// edit -> save -> load round-trip through the package format. No Godot dependency — this is exactly
/// the surface the layering split keeps testable outside the engine.
/// </summary>
[TestFixture]
public sealed class EditableLevelTests
{
    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 3;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath WaterPath = ResourcePath.Create("tiles/water.png");

    // ----- apply-edit -----

    [Test]
    public void PaintCell_SetsTileAndReportsChange()
    {
        var session = new LevelEditSession(SampleLevel());

        var change = session.PaintCell(0, 1, 2, tileId: 1);

        Assert.Multiple(() =>
        {
            Assert.That(change, Is.EqualTo(new CellChange(0, 1, 2, 1)));
            Assert.That(session.Level.GetCell(0, 1, 2), Is.EqualTo(1));
            Assert.That(session.IsDirty, Is.True);
        });
    }

    [Test]
    public void PaintCell_WhenCellAlreadyHoldsTile_IsNoOp()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);

        var second = session.PaintCell(0, 0, 0, 1);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Null);
            Assert.That(session.CanUndo, Is.True);
            // Only the first paint is on the history — the redundant one did not stack.
            session.Undo();
            Assert.That(session.CanUndo, Is.False);
        });
    }

    [Test]
    public void EraseCell_ClearsToEmpty()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 3, 1, 1);

        var change = session.EraseCell(0, 3, 1);

        Assert.Multiple(() =>
        {
            Assert.That(change, Is.EqualTo(new CellChange(0, 3, 1, LayerDefinition.EmptyCell)));
            Assert.That(session.Level.GetCell(0, 3, 1), Is.EqualTo(LayerDefinition.EmptyCell));
        });
    }

    [Test]
    public void PaintCell_OutOfBounds_ReturnsNull()
    {
        var session = new LevelEditSession(SampleLevel());
        Assert.That(session.PaintCell(0, Width, 0, 1), Is.Null);
        Assert.That(session.IsDirty, Is.False);
    }

    [Test]
    public void PaintCell_WithTileOutsidePalette_Throws()
    {
        var session = new LevelEditSession(SampleLevel());
        Assert.Throws<ArgumentException>(() => session.PaintCell(0, 0, 0, tileId: 99));
    }

    [Test]
    public void PaintCell_WithInvalidLayer_Throws()
    {
        var session = new LevelEditSession(SampleLevel());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.PaintCell(5, 0, 0, 1));
    }

    // ----- undo / redo -----

    [Test]
    public void Undo_RestoresPreviousValue_Redo_Reapplies()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 2, 2, 1);

        var undo = session.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(undo, Is.EqualTo(new CellChange(0, 2, 2, LayerDefinition.EmptyCell)));
            Assert.That(session.Level.GetCell(0, 2, 2), Is.EqualTo(LayerDefinition.EmptyCell));
            Assert.That(session.CanRedo, Is.True);
        });

        var redo = session.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(redo, Is.EqualTo(new CellChange(0, 2, 2, 1)));
            Assert.That(session.Level.GetCell(0, 2, 2), Is.EqualTo(1));
        });
    }

    [Test]
    public void NewEdit_ClearsRedoStack()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        session.Undo();
        Assert.That(session.CanRedo, Is.True);

        session.PaintCell(0, 1, 1, 5);

        Assert.That(session.CanRedo, Is.False);
    }

    [Test]
    public void Undo_WithNothingToUndo_ReturnsNull()
    {
        var session = new LevelEditSession(SampleLevel());
        Assert.That(session.Undo(), Is.Null);
        Assert.That(session.Redo(), Is.Null);
    }

    [Test]
    public void History_IsBoundedToMaxDepth()
    {
        var level = SampleLevel();
        var history = new EditHistory();
        // Execute more commands than the cap, each on the same cell so values are deterministic.
        for (var i = 0; i < EditHistory.MaxDepth + 50; i++)
            history.Execute(new SetCellCommand(0, 0, 0, i % 2 == 0 ? 1 : 2), level);

        var undone = 0;
        while (history.Undo(level) is not null)
            undone++;

        Assert.That(undone, Is.EqualTo(EditHistory.MaxDepth));
    }

    // ----- create blank -----

    [Test]
    public void CreateBlank_ProducesEmptyPaintableLevel()
    {
        var level = EditableLevel.CreateBlank("Untitled", TileSize, 5, 4, ResourceReference.ToSelf(TileSetPath), Palette());

        Assert.Multiple(() =>
        {
            Assert.That(level.Width, Is.EqualTo(5));
            Assert.That(level.Height, Is.EqualTo(4));
            Assert.That(level.Layers, Has.Count.EqualTo(1));
            Assert.That(level.Layers[0].Cells, Has.Length.EqualTo(20));
            Assert.That(level.Layers[0].Cells, Is.All.EqualTo(LayerDefinition.EmptyCell));
            Assert.That(level.Tiles, Has.Count.EqualTo(2));
            // Package-as-VFS correction (DiVoid #7571/#7572): a blank level starts unattached — it has
            // provisional, slug-derived paths but no stable package slot until it goes through Save-As.
            Assert.That(level.IsAttached, Is.False);
            Assert.That(level.LevelPath, Is.EqualTo(LevelResourcePaths.LevelPath("untitled")));
        });
    }

    // ----- edit -> save -> load round-trip -----

    [Test]
    public void EditSaveLoad_RoundTripsEditedCells()
    {
        var packageBytes = BuildSamplePackageBytes();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var originalPackageId = package.Id;
        var original = EditableLevelReader.FromPackage(package);
        var session = new LevelEditSession(original);

        // Paint a grass tile, paint a water tile, then erase a pre-existing cell.
        session.PaintCell(0, 0, 0, 1);
        session.PaintCell(0, 3, 2, 5);
        session.EraseCell(0, 1, 1); // (1,1) held grass id 1 in the sample

        // Package-as-VFS correction (DiVoid #7571/#7572): a plain re-save merges into the ALREADY-OPEN
        // source package rather than fabricating a fresh one — this level is attached (loaded from a real
        // resource), so Save reuses its own paths and the archive's identity is carried forward untouched.
        var savedBytes = session.Save(package);
        Assert.That(session.IsDirty, Is.False);

        var reloaded = EditableLevelReader.FromPackageBytes(savedBytes);
        using var reloadedPackage = PackageReader.Open(new MemoryStream(savedBytes));

        Assert.Multiple(() =>
        {
            // Edited cells persisted.
            Assert.That(reloaded.GetCell(0, 0, 0), Is.EqualTo(1));
            Assert.That(reloaded.GetCell(0, 3, 2), Is.EqualTo(5));
            Assert.That(reloaded.GetCell(0, 1, 1), Is.EqualTo(LayerDefinition.EmptyCell));

            // Untouched cell intact.
            Assert.That(reloaded.GetCell(0, 2, 1), Is.EqualTo(2));

            // Geometry, palette, metadata, spawns preserved.
            Assert.That(reloaded.Width, Is.EqualTo(Width));
            Assert.That(reloaded.Height, Is.EqualTo(Height));
            Assert.That(reloaded.TileSize, Is.EqualTo(TileSize));
            Assert.That(reloadedPackage.Id, Is.EqualTo(originalPackageId));
            Assert.That(reloaded.Name, Is.EqualTo(original.Name));
            Assert.That(reloaded.Tiles, Has.Count.EqualTo(original.Tiles.Count));
            Assert.That(reloaded.Spawns.ContainsKey("start"), Is.True);
            Assert.That(reloaded.DefaultSpawn, Is.EqualTo("start"));
            Assert.That(reloaded.Layers[0].Collision, Is.True);
        });
    }

    [Test]
    public void Save_LoadsThroughTheRuntimeLoaderToo()
    {
        // The saved package must also be readable by the play-time LevelLoader, not just the editor.
        var packageBytes = BuildSamplePackageBytes();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var session = new LevelEditSession(EditableLevelReader.FromPackage(package));
        session.PaintCell(0, 0, 0, 5);
        var savedBytes = session.Save(package);

        using var registry = new PackageRegistry(PackageReader.Open(new MemoryStream(savedBytes)));
        var resolved = LevelLoader.Load(registry, ResourceReference.ToSelf(LevelPath));

        Assert.That(resolved.Layers[0].Cells[0], Is.EqualTo(5));
    }

    [Test]
    public void ToResolvedLevel_ProjectsGraphicsAndCollision()
    {
        var level = EditableLevelReader.FromPackageBytes(BuildSamplePackageBytes());
        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TileGraphics, Contains.Key(1));
            Assert.That(resolved.TileGraphics, Contains.Key(5));
            Assert.That(resolved.CollidingTileIds, Contains.Item(1)); // grass collides
            Assert.That(resolved.CollidingTileIds, Does.Not.Contain(5)); // water does not
        });
    }

    [Test]
    public void Reader_WhenNoLevelResource_Throws()
    {
        var builder = new PackageBuilder().WithName("Empty");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, Encoding.UTF8.GetBytes("{}"));
        var bytes = ToBytes(builder);

        Assert.Throws<LevelContentException>(() => EditableLevelReader.FromPackageBytes(bytes));
    }

    [Test]
    public void Reader_WhenTileSetInAnotherPackage_Throws()
    {
        var foreign = PackageId.New();
        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = 1,
            Height = 1,
            TileSet = new ResourceReference(foreign, TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "t", Cells = new[] { LayerDefinition.EmptyCell } } },
        };
        var builder = new PackageBuilder().WithName("Cross");
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        var exception = Assert.Throws<LevelContentException>(() => EditableLevelReader.FromPackageBytes(ToBytes(builder)));
        Assert.That(exception!.Message, Does.Contain("another package"));
    }

    [Test]
    public void Reader_WhenTileGraphicInAnotherPackage_Throws()
    {
        var foreign = PackageId.New();
        var tileSet = new TileSetDefinition
        {
            Tiles = new[] { new TileDefinition { Id = 1, Graphic = new ResourceReference(foreign, GrassPath) } },
        };
        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = 1,
            Height = 1,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Layers = new[] { new LayerDefinition { Name = "t", Cells = new[] { LayerDefinition.EmptyCell } } },
        };
        var builder = new PackageBuilder().WithName("Cross");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));

        var exception = Assert.Throws<LevelContentException>(() => EditableLevelReader.FromPackageBytes(ToBytes(builder)));
        Assert.That(exception!.Message, Does.Contain("cross-package"));
    }

    [Test]
    public void RoundTrip_PreservesBackgroundColor()
    {
        // Package-as-VFS correction (DiVoid #7571/#7572): ForkedFrom/Attribution moved OFF EditableLevel
        // onto PackageContext (archive identity) — their round-trip-through-a-merge is covered by
        // LevelMergeWriterTests instead. BackgroundColor stays level content, so it still belongs here.
        var packageBytes = BuildSamplePackageBytes();
        using var package = PackageReader.Open(new MemoryStream(packageBytes));
        var origin = EditableLevelReader.FromPackage(package);
        var withBackground = new EditableLevel(
            origin.Name, origin.LevelPath, origin.TileSetReference, origin.TileSize, origin.Width, origin.Height,
            "#3A5A8C", origin.Spawns, origin.DefaultSpawn, origin.Tiles, origin.Layers, origin.TileScripts, isAttached: true);
        var session = new LevelEditSession(withBackground);

        var reloaded = EditableLevelReader.FromPackageBytes(session.Save(package));

        Assert.That(reloaded.BackgroundColor, Is.EqualTo("#3A5A8C"));
    }

    [Test]
    public void Snapshot_ParsesBackgroundColor()
    {
        var origin = EditableLevelReader.FromPackageBytes(BuildSamplePackageBytes());
        var withBackground = new EditableLevel(
            origin.Name, origin.LevelPath, origin.TileSetReference, origin.TileSize, origin.Width, origin.Height,
            "#204080", origin.Spawns, origin.DefaultSpawn, origin.Tiles, origin.Layers, origin.TileScripts, isAttached: true);

        var resolved = EditableLevelSnapshot.ToResolvedLevel(withBackground);

        Assert.That(resolved.BackgroundColor, Is.Not.Null);
    }

    [Test]
    public void CreateBlank_SavesAndReloads()
    {
        // Shared-tileset correction (DiVoid #7551 Phase 1a): a level's own save no longer carries its
        // tileset — the bound tile set must be saved too (its own contributions), exactly as
        // LevelEditor.SaveLevelAndTileSet orchestrates, or the reference the level.json carries dangles.
        var tileSet = EditableTileSet.CreateBlank("Untitled Tiles", Palette());
        var tileSetSession = new TileSetEditSession(tileSet);
        tileSetSession.AttachAsNewResource(Array.Empty<ResourceEntry>());

        var session = new LevelEditSession(EditableLevel.CreateBlank(
            "Untitled", TileSize, 3, 3, ResourceReference.ToSelf(tileSet.TileSetPath), Palette()));
        session.PaintCell(0, 1, 1, 1);
        var reloaded = EditableLevelReader.FromPackageBytes(session.SaveFresh("Untitled Package", tileSetSession.BuildContributions()));

        Assert.That(reloaded.GetCell(0, 1, 1), Is.EqualTo(1));
        Assert.That(reloaded.Layers[0].Cells, Has.Length.EqualTo(9));
    }

    [Test]
    public void Save_ThenMarkDirty_IsDirtyAgain()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        session.SaveFresh("Sample Package");
        Assert.That(session.IsDirty, Is.False);

        session.MarkDirty();

        Assert.That(session.IsDirty, Is.True);
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
            Palette(), new[] { layer }, new Dictionary<ResourcePath, string>());
    }

    // A real self-contained package: grass at (1,1) and (2,1), everything else empty.
    private static byte[] BuildSamplePackageBytes()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        cells[1 * Width + 1] = 1; // (1,1) grass
        cells[1 * Width + 2] = 2; // (2,1) — but tile 2 must exist; add a third palette tile

        var tiles = new[]
        {
            new TileDefinition { Id = 1, Graphic = ResourceReference.ToSelf(GrassPath), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.Full },
            new TileDefinition { Id = 2, Graphic = ResourceReference.ToSelf(ResourcePath.Create("tiles/dirt.png")), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.Full },
            new TileDefinition { Id = 5, Graphic = ResourceReference.ToSelf(WaterPath), CollisionShape = Uberkarl.Content.CollisionShapeDefinition.None },
        };
        var tileSet = new TileSetDefinition { Tiles = tiles };

        var level = new LevelDefinition
        {
            TileSize = TileSize,
            Width = Width,
            Height = Height,
            TileSet = ResourceReference.ToSelf(TileSetPath),
            Spawns = new Dictionary<string, GridPosition> { ["start"] = new GridPosition(0, 0) },
            DefaultSpawn = "start",
            Layers = new[]
            {
                new LayerDefinition { Name = "terrain", Collision = true, Cells = cells },
            },
        };

        var builder = new PackageBuilder().WithName("Demo Pack").WithVersion("0.1.0");
        builder.AddResource(ResourceKind.TileGraphic, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, ResourcePath.Create("tiles/dirt.png"), Encoding.UTF8.GetBytes("DIRT-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileGraphic, WaterPath, Encoding.UTF8.GetBytes("WATER-PNG"), "image/png");
        builder.AddResource(ResourceKind.TileSet, TileSetPath, LevelContentSerializer.WriteTileSet(tileSet));
        builder.AddResource(ResourceKind.Level, LevelPath, LevelContentSerializer.WriteLevel(level));
        return ToBytes(builder);
    }

    private static byte[] ToBytes(PackageBuilder builder)
    {
        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }
}
