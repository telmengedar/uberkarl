using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Editor;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the level-naming seam the package browser's Save-As flow relies on (DiVoid #7552):
/// <see cref="EditableLevel.Rename"/> (the model mutation) and <see cref="LevelEditSession.RenameLevel"/>
/// (the session-level intent, mirroring <see cref="LevelEditSession.RenameLayer"/>'s blank-is-no-op
/// guard). No Godot dependency — the Godot glue (<c>game/Editor/PackageBrowser.cs</c>,
/// <c>game/Editor/LevelEditor.cs</c>) only calls this and reuses the existing write paths.
/// </summary>
[TestFixture]
public sealed class LevelSaveTests
{
    private const int TileSize = 16;
    private const int Width = 4;
    private const int Height = 3;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");

    // ----- EditableLevel.Rename -----

    [Test]
    public void Rename_ReplacesName_ReturnsTrue()
    {
        var level = SampleLevel();

        var happened = level.Rename("New Name");

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(level.Name, Is.EqualTo("New Name"));
        });
    }

    [Test]
    public void Rename_SameName_IsNoOp()
    {
        var level = SampleLevel(); // named "Sample"
        Assert.That(level.Rename("Sample"), Is.False);
    }

    [Test]
    public void Rename_EmptyName_Throws()
    {
        var level = SampleLevel();
        Assert.Throws<ArgumentException>(() => level.Rename(""));
    }

    [Test]
    public void Rename_DoesNotTouchAnyOtherProperty()
    {
        var level = SampleLevel();
        var layersBefore = level.Layers;
        var tilesBefore = level.Tiles;

        level.Rename("Renamed");

        Assert.Multiple(() =>
        {
            Assert.That(level.Layers, Is.SameAs(layersBefore));
            Assert.That(level.Tiles, Is.SameAs(tilesBefore));
            Assert.That(level.Width, Is.EqualTo(Width));
            Assert.That(level.Height, Is.EqualTo(Height));
        });
    }

    // ----- LevelEditSession.RenameLevel -----

    [Test]
    public void Session_RenameLevel_AppliesTrimmedName_MarksDirty()
    {
        var session = new LevelEditSession(SampleLevel());

        var happened = session.RenameLevel("  My Cool Level  ");

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(session.Level.Name, Is.EqualTo("My Cool Level"));
            Assert.That(session.IsDirty, Is.True);
        });
    }

    [Test]
    public void Session_RenameLevel_BlankOrWhitespaceOnly_IsNoOp_DoesNotMarkDirty()
    {
        var session = new LevelEditSession(SampleLevel());

        var blank = session.RenameLevel("");
        var whitespace = session.RenameLevel("   ");

        Assert.Multiple(() =>
        {
            Assert.That(blank, Is.False);
            Assert.That(whitespace, Is.False);
            Assert.That(session.Level.Name, Is.EqualTo("Sample"));
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_RenameLevel_SameNameAfterTrim_IsNoOp_DoesNotMarkDirty()
    {
        var session = new LevelEditSession(SampleLevel());

        var happened = session.RenameLevel("  Sample  ");

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_RenameLevel_PreservesCellEditHistory()
    {
        var session = new LevelEditSession(SampleLevel());
        session.PaintCell(0, 0, 0, 1);
        Assert.That(session.CanUndo, Is.True);

        session.RenameLevel("Renamed");

        Assert.That(session.CanUndo, Is.True, "renaming the level must not disturb cell-edit history.");
    }

    [Test]
    public void Session_RenameLevel_ThenAttachThenSaveFresh_RoundTripsAsItsOwnNamespacedResource()
    {
        // Package-as-VFS correction (DiVoid #7571/#7572): a level's display name is content, not a
        // package's manifest name — SampleLevel() is unattached, so the flow a real Save-As drives is
        // rename -> attach (derives levels/<slug>.json from the new name) -> save. The reloaded level's
        // name (like the browser's existing resource list) is derived from ITS resource path, so it comes
        // back slug-shaped rather than the exact typed string — that is the new, honest contract, not a
        // round-trip bug: package identity no longer carries a level's display name at all.
        // Shared-tileset correction (DiVoid #7551 Phase 1a): SampleLevel() binds a tile set reference that
        // must itself land in the saved package too, or the level's reference dangles — mirrors
        // LevelEditor.SaveLevelAndTileSet's combined save.
        var tileSet = EditableTileSet.CreateBlank("Sample Tiles", Palette());
        var tileSetSession = new TileSetEditSession(tileSet);
        tileSetSession.AttachToExistingResource(TileSetPath); // SampleLevel()'s reference is fixed to this exact path

        var session = new LevelEditSession(SampleLevel());
        session.RenameLevel("Saved Under New Name");
        session.AttachAsNewResource(Array.Empty<ResourceEntry>());

        var reloaded = EditableLevelReader.FromPackageBytes(session.SaveFresh("Some Package", tileSetSession.BuildContributions()));

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.LevelPath, Is.EqualTo(LevelResourcePaths.LevelPath("saved-under-new-name")));
            Assert.That(reloaded.Name, Is.EqualTo("saved-under-new-name"));
        });
    }

    // ----- helpers -----

    private static IReadOnlyList<EditableTile> Palette() => new[]
    {
        new EditableTile(1, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.Full),
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
}
