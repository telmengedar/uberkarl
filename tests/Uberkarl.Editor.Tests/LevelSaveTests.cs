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
    public void Session_RenameLevel_ThenSave_RoundTripsTheNewNameAsThePackagesManifestName()
    {
        var session = new LevelEditSession(SampleLevel());
        session.RenameLevel("Saved Under New Name");

        var reloaded = EditableLevelReader.FromPackageBytes(session.Save());

        Assert.That(reloaded.Name, Is.EqualTo("Saved Under New Name"));
    }

    // ----- helpers -----

    private static IReadOnlyList<EditableTile> Palette() => new[]
    {
        new EditableTile(1, GrassPath, Encoding.UTF8.GetBytes("GRASS-PNG"), collides: true),
    };

    private static EditableLevel SampleLevel()
    {
        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        return new EditableLevel(
            PackageId.New(), "Sample", "0.1.0", null, null, LevelPath, TileSetPath,
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            Palette(), new[] { layer });
    }
}
