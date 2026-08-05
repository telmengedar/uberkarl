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

        var first = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.Full);
        var second = tileSet.AddTile(Png("B"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

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
        var seed = new[] { new EditableTile(1, ResourcePath.Create("graphics/x/1.png"), Png("A"), Uberkarl.Content.CollisionShapeDefinition.Full), new EditableTile(5, ResourcePath.Create("graphics/x/5.png"), Png("B"), Uberkarl.Content.CollisionShapeDefinition.None) };
        var tileSet = EditableTileSet.CreateBlank("Untitled", seed);

        var id = tileSet.AddTile(Png("C"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.Full);

        Assert.That(id, Is.EqualTo(6), "the next id must be past the HIGHEST existing id, not the count.");
    }

    [Test]
    public void AddTile_RejectsEmptyGraphic()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.Throws<ArgumentException>(() => tileSet.AddTile(Array.Empty<byte>(), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None));
    }

    [Test]
    public void AddTile_SetsAProvisionalGraphicPath_DerivedFromTheCurrentName()
    {
        var tileSet = EditableTileSet.CreateBlank("Forest Set");

        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        Assert.That(tileSet.Tiles[0].GraphicPath, Is.EqualTo(TileSetResourcePaths.GraphicPath("forest-set", id)));
    }

    // ----- RemoveTile / id stability -----

    [Test]
    public void RemoveTile_DropsTheTile_ReturnsTrue()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

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
        var first = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        var second = tileSet.AddTile(Png("B"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.RemoveTile(second);

        var third = tileSet.AddTile(Png("C"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        Assert.That(third, Is.Not.EqualTo(second), "a removed id must never be reissued.");
        Assert.That(third, Is.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(tileSet.Contains(first), Is.True);
            Assert.That(tileSet.Contains(second), Is.False);
            Assert.That(tileSet.Contains(third), Is.True);
        });
    }

    // ----- RenameTile / SetTileCollisionShape -----

    [Test]
    public void RenameTile_SetsTheName_ReturnsTrue()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

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
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
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
    public void SetTileCollisionShape_ChangesTheShape_NoOpWhenUnchanged()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        var changed = tileSet.SetTileCollisionShape(id, Uberkarl.Content.CollisionShapeDefinition.Full);
        var noOp = tileSet.SetTileCollisionShape(id, Uberkarl.Content.CollisionShapeDefinition.Full);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(noOp, Is.False);
            Assert.That(tileSet.Tiles[0].CollisionShape.Kind, Is.EqualTo(Uberkarl.Content.CollisionShapeKind.Full));
        });
    }

    [Test]
    public void SetTileCollisionShape_ToAPreset_NoOpWhenTheSamePresetIsSetAgain_ButChangesForADifferentPreset()
    {
        // DiVoid #7551 Phase 4: presets carry data beyond Kind (which named preset), so the no-op check
        // must compare the preset itself, not just Kind — this is exactly what TileSetEditor's cycle button
        // relies on to detect "wrapped back to the same shape."
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        var changed = tileSet.SetTileCollisionShape(id, Uberkarl.Content.CollisionShapeDefinition.FromPreset(Uberkarl.Content.CollisionPreset.SlopeLeft));
        var noOp = tileSet.SetTileCollisionShape(id, Uberkarl.Content.CollisionShapeDefinition.FromPreset(Uberkarl.Content.CollisionPreset.SlopeLeft));
        var changedAgain = tileSet.SetTileCollisionShape(id, Uberkarl.Content.CollisionShapeDefinition.FromPreset(Uberkarl.Content.CollisionPreset.SlopeRight));

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(noOp, Is.False);
            Assert.That(changedAgain, Is.True);
            Assert.That(tileSet.Tiles[0].CollisionShape.Preset, Is.EqualTo(Uberkarl.Content.CollisionPreset.SlopeRight));
        });
    }

    [Test]
    public void SetTileCollisionShape_UnknownTileId_IsNoOp_ReturnsFalse()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");

        Assert.That(tileSet.SetTileCollisionShape(99, Uberkarl.Content.CollisionShapeDefinition.Full), Is.False);
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

        session.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

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
        session.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        session.MarkSaved();
        Assert.That(session.IsDirty, Is.False);

        session.MarkDirty();
        Assert.That(session.IsDirty, Is.True);
    }

    // ----- AddFrame / RemoveFrame / SetAnimationSpeed — DiVoid #7551 Phase 2, design #7580 -----
    // The simple<->animated structural transition: a tile carries no "kind" flag of its own — Frames.Count
    // being non-zero IS animated (EditableTile.IsAnimated), so these tests pin that transition directly.

    [Test]
    public void NewTile_IsNotAnimated()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        Assert.That(tileSet.Tiles.First(tile => tile.Id == id).IsAnimated, Is.False);
    }

    [Test]
    public void AddFrame_ToASimpleTile_MakesItAnimated_TheSimpleToAnimatedTransition()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        var happened = tileSet.AddFrame(id, Png("B"));

        var tile = tileSet.Tiles.First(t => t.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(tile.IsAnimated, Is.True);
            Assert.That(tile.Frames, Has.Count.EqualTo(1));
            Assert.That(tile.Frames[0].Graphic, Is.EqualTo(Png("B")));
            Assert.That(tile.Graphic, Is.EqualTo(Png("A")), "the original graphic stays frame 0.");
        });
    }

    [Test]
    public void AddFrame_Twice_KeepsFramesInAppendOrder()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        tileSet.AddFrame(id, Png("B"));
        tileSet.AddFrame(id, Png("C"));

        var tile = tileSet.Tiles.First(t => t.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(tile.Frames, Has.Count.EqualTo(2));
            Assert.That(tile.Frames[0].Graphic, Is.EqualTo(Png("B")));
            Assert.That(tile.Frames[1].Graphic, Is.EqualTo(Png("C")));
        });
    }

    [Test]
    public void AddFrame_UnknownTileId_IsNoOp_ReturnsFalse()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.That(tileSet.AddFrame(99, Png("A")), Is.False);
    }

    [Test]
    public void AddFrame_RejectsEmptyGraphic()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        Assert.Throws<ArgumentException>(() => tileSet.AddFrame(id, Array.Empty<byte>()));
    }

    [Test]
    public void AddFrame_SetsAProvisionalFramePath_DistinctFromTheGraphicPath()
    {
        var tileSet = EditableTileSet.CreateBlank("Forest Set");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        tileSet.AddFrame(id, Png("B"));

        var tile = tileSet.Tiles.First(t => t.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(tile.Frames[0].GraphicPath, Is.EqualTo(TileSetResourcePaths.FramePath("forest-set", id, 2)));
            Assert.That(tile.Frames[0].GraphicPath, Is.Not.EqualTo(tile.GraphicPath));
        });
    }

    [Test]
    public void RemoveFrame_DropsExactlyThatFrame_KeepsOthers()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.AddFrame(id, Png("B"));
        tileSet.AddFrame(id, Png("C"));

        var happened = tileSet.RemoveFrame(id, 0); // drops "B", keeps "C"

        var tile = tileSet.Tiles.First(t => t.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(tile.Frames, Has.Count.EqualTo(1));
            Assert.That(tile.Frames[0].Graphic, Is.EqualTo(Png("C")));
        });
    }

    [Test]
    public void RemoveFrame_TheOnlyFrame_MakesTheTileSimpleAgain_TheAnimatedToSimpleTransition()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.AddFrame(id, Png("B"));

        var happened = tileSet.RemoveFrame(id, 0);

        var tile = tileSet.Tiles.First(t => t.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(tile.IsAnimated, Is.False);
            Assert.That(tile.Frames, Is.Empty);
            Assert.That(tile.Graphic, Is.EqualTo(Png("A")), "the tile itself (frame 0) survives — only the extra frame is gone.");
        });
    }

    [Test]
    public void RemoveFrame_UnknownTileId_IsNoOp_ReturnsFalse()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.That(tileSet.RemoveFrame(99, 0), Is.False);
    }

    [Test]
    public void RemoveFrame_OutOfRangeIndex_IsNoOp_ReturnsFalse()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.AddFrame(id, Png("B"));

        Assert.Multiple(() =>
        {
            Assert.That(tileSet.RemoveFrame(id, -1), Is.False);
            Assert.That(tileSet.RemoveFrame(id, 1), Is.False);
        });
    }

    [Test]
    public void SetAnimationSpeed_ChangesTheSpeed_NoOpWhenUnchanged()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        var changed = tileSet.SetAnimationSpeed(id, 12.0);
        var noOp = tileSet.SetAnimationSpeed(id, 12.0);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(noOp, Is.False);
            Assert.That(tileSet.Tiles.First(t => t.Id == id).AnimationSpeed, Is.EqualTo(12.0));
        });
    }

    [Test]
    public void SetAnimationSpeed_NonPositive_Throws()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);

        Assert.Throws<ArgumentException>(() => tileSet.SetAnimationSpeed(id, 0));
    }

    [Test]
    public void SetAnimationSpeed_UnknownTileId_IsNoOp_ReturnsFalse()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.That(tileSet.SetAnimationSpeed(99, 12.0), Is.False);
    }

    [Test]
    public void Attach_RemapsFramePaths_ToTheTileSetsOwnNamespace()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var id = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.AddFrame(id, Png("B"));
        tileSet.AddFrame(id, Png("C"));

        tileSet.Attach("forest-tiles", overwriteTileSetPath: null);

        var tile = tileSet.Tiles.First(t => t.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(tile.GraphicPath, Is.EqualTo(TileSetResourcePaths.GraphicPath("forest-tiles", id)));
            Assert.That(tile.Frames[0].GraphicPath, Is.EqualTo(TileSetResourcePaths.FramePath("forest-tiles", id, 2)));
            Assert.That(tile.Frames[1].GraphicPath, Is.EqualTo(TileSetResourcePaths.FramePath("forest-tiles", id, 3)));
        });
    }

    // ----- TileSetEditSession: frame/speed intents + dirty tracking -----

    [Test]
    public void Session_AddFrame_MarksDirty()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled"));
        var id = session.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        session.MarkSaved();

        var happened = session.AddFrame(id, Png("B"));

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(session.IsDirty, Is.True);
            Assert.That(session.TileSet.Tiles[0].IsAnimated, Is.True);
        });
    }

    [Test]
    public void Session_RemoveFrame_UnknownTileId_DoesNotMarkDirty()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled"));

        var happened = session.RemoveFrame(1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(session.IsDirty, Is.False);
        });
    }

    [Test]
    public void Session_SetAnimationSpeed_MarksDirty()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled"));
        var id = session.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        session.MarkSaved();

        var happened = session.SetAnimationSpeed(id, 16.0);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(session.IsDirty, Is.True);
        });
    }
}
