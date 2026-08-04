using System.Text;
using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers <see cref="EditableTileSet"/> and <see cref="TileSetEditSession"/> — the standalone shared
/// tile set authoring model (DiVoid #7551 Phase 1b, design #7580): add/remove/rename a simple tile,
/// toggle collision, and the id-stability guarantee (design #7580 §11) that keeps a removed tile's id
/// from ever aliasing onto a later-added one. Engine-agnostic — no Godot, no package IO (that is
/// <c>EditableTileSetReader</c>/<c>TileSetMergeWriter</c>'s job, covered in <c>PackageVfsSaveModelTests</c>).
/// </summary>
[TestFixture]
public sealed class TileSetAuthoringTests
{
    private static byte[] Png(string marker) => Encoding.UTF8.GetBytes(marker);

    // ----- AddTile -----

    [Test]
    public void AddTile_MintsSequentialIds_StartingAtOne_ForAnEmptyTileSet()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");

        var first = tileSet.AddTile(Png("A"), collides: true);
        var second = tileSet.AddTile(Png("B"), collides: false);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
            Assert.That(tileSet.Tiles, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AddTile_ContinuesPastTheHighestExistingId_WhenSeededWithInitialTiles()
    {
        var seed = new[] { new EditableTile(1, ResourcePath.Create("graphics/x/1.png"), Png("A"), true), new EditableTile(5, ResourcePath.Create("graphics/x/5.png"), Png("B"), false) };
        var tileSet = EditableTileSet.CreateBlank("Untitled", seed);

        var id = tileSet.AddTile(Png("C"), collides: true);

        Assert.That(id, Is.EqualTo(6), "the next id must be past the HIGHEST existing id, not the count.");
    }

    [Test]
    public void AddTile_RejectsEmptyGraphic()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.Throws<ArgumentException>(() => tileSet.AddTile(Array.Empty<byte>(), collides: false));
    }

    [Test]
    public void AddTile_SetsAProvisionalGraphicPath_DerivedFromTheCurrentName()
    {
        var tileSet = EditableTileSet.CreateBlank("Forest Set");

        var id = tileSet.AddTile(Png("A"), collides: false);

        Assert.That(tileSet.Tiles[0].GraphicPath, Is.EqualTo(TileSetResourcePaths.GraphicPath("forest-set", id)));
    }

    // ----- RemoveTile / id stability -----

    [Test]
    public void RemoveTile_DropsTheTile_ReturnsTrue()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collides: false);

        var happened = tileSet.RemoveTile(id);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(tileSet.Contains(id), Is.False);
            Assert.That(tileSet.Tiles, Is.Empty);
        });
    }

    [Test]
    public void RemoveTile_UnknownId_IsNoOp_ReturnsFalse()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.That(tileSet.RemoveTile(42), Is.False);
    }

    [Test]
    public void RemoveTile_ThenAddTile_NeverReusesTheRemovedId()
    {
        // Design #7580 §11 risk: a stale reference to a removed tile id must never silently alias onto a
        // DIFFERENT, later-added tile.
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var first = tileSet.AddTile(Png("A"), collides: false);
        var second = tileSet.AddTile(Png("B"), collides: false);
        tileSet.RemoveTile(second);

        var third = tileSet.AddTile(Png("C"), collides: false);

        Assert.That(third, Is.Not.EqualTo(second), "a removed id must never be reissued.");
        Assert.That(third, Is.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(tileSet.Contains(first), Is.True);
            Assert.That(tileSet.Contains(second), Is.False);
            Assert.That(tileSet.Contains(third), Is.True);
        });
    }

    // ----- RenameTile / SetTileCollides -----

    [Test]
    public void RenameTile_SetsTheName_ReturnsTrue()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collides: false);

        var happened = tileSet.RenameTile(id, "Grass");

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(tileSet.Tiles[0].Name, Is.EqualTo("Grass"));
        });
    }

    [Test]
    public void RenameTile_BlankName_NormalizesToNull()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collides: false);
        tileSet.RenameTile(id, "Grass");

        tileSet.RenameTile(id, "   ");

        Assert.That(tileSet.Tiles[0].Name, Is.Null);
    }

    [Test]
    public void RenameTile_UnknownId_IsNoOp()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.That(tileSet.RenameTile(99, "Nope"), Is.False);
    }

    [Test]
    public void SetTileCollides_TogglesTheFlag_NoOpWhenUnchanged()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collides: false);

        var changed = tileSet.SetTileCollides(id, true);
        var noOp = tileSet.SetTileCollides(id, true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(noOp, Is.False);
            Assert.That(tileSet.Tiles[0].Collides, Is.True);
        });
    }

    // ----- Rename (the tile set's own display name) -----

    [Test]
    public void Rename_SameName_IsNoOp()
    {
        var tileSet = EditableTileSet.CreateBlank("Sample");
        Assert.That(tileSet.Rename("Sample"), Is.False);
    }

    [Test]
    public void Rename_EmptyName_Throws()
    {
        var tileSet = EditableTileSet.CreateBlank("Sample");
        Assert.Throws<ArgumentException>(() => tileSet.Rename(""));
    }

    // ----- TileSetEditSession: dirty tracking -----

    [Test]
    public void Session_AddTile_MarksDirty()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled"));
        Assert.That(session.IsDirty, Is.False);

        session.AddTile(Png("A"), collides: false);

        Assert.That(session.IsDirty, Is.True);
    }

    [Test]
    public void Session_RemoveTile_UnknownId_DoesNotMarkDirty()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled"));

        var happened = session.RemoveTile(1);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_MarkSaved_ThenMarkDirty_RoundTrips()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled"));
        session.AddTile(Png("A"), collides: false);

        session.MarkSaved();
        Assert.That(session.IsDirty, Is.False);

        session.MarkDirty();
        Assert.That(session.IsDirty, Is.True);
    }
}
