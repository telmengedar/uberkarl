using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the terrain/meta-tile (auto-tiling) authoring model (DiVoid #7551 Phase 3, design #7580):
/// <see cref="EditableTileSet"/>'s terrain-set/terrain CRUD and per-tile membership/peering-bit
/// assignment, <see cref="TileSetEditSession"/>'s wrapping of those as dirty-tracked intents,
/// <see cref="LevelEditSession"/>'s terrain brush (<see cref="LevelEditSession.PaintTerrain"/>/
/// <see cref="LevelEditSession.EraseTerrain"/>) and its two-channel (concrete XOR terrain) invariant with
/// undo/redo, the <see cref="EditableLevelSnapshot"/> projection carrying terrain data into
/// <see cref="ResolvedLevel"/> (the exact P2 bug class — "the snapshot dropping new data" — this project
/// guards against for every tile capability), and the package round-trip through
/// <see cref="EditableTileSetReader"/>/<see cref="TileSetMergeWriter"/> and
/// <see cref="EditableLevelReader"/>/<see cref="LevelMergeWriter"/>. Engine-agnostic — no Godot; the Godot
/// terrain-set/peering-bit mapping and terrain-connect resolution are verified in-engine (see
/// <c>Uberkarl.Content.Tests.TerrainTileTests</c>'s doc comment for the project's convention here).
/// </summary>
[TestFixture]
public sealed class TerrainAuthoringTests
{
    private const int TileSize = 16;
    private const int Width = 3;
    private const int Height = 1;

    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");
    private static readonly ResourcePath GrassPath = ResourcePath.Create("tiles/grass.png");
    private static readonly ResourcePath EarthPath = ResourcePath.Create("tiles/earth.png");

    private static byte[] Png(string marker) => Encoding.UTF8.GetBytes(marker);

    // ----- EditableTileSet: terrain set / terrain CRUD -----

    [Test]
    public void AddTerrainSet_MintsAnId_AndIsListed()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");

        var id = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo(1));
            Assert.That(tileSet.TerrainSets, Has.Count.EqualTo(1));
            Assert.That(tileSet.TerrainSets[0].Name, Is.EqualTo("Ground"));
            Assert.That(tileSet.TerrainSets[0].MatchingMode, Is.EqualTo(TerrainMatchMode.CornersAndSides));
        });
    }

    [Test]
    public void AddTerrain_MintsAnIdUniqueAcrossTheWholeTileSet_NotJustItsOwnSet()
    {
        // Design #7580 §11: terrain ids must be stable/never-reused across the WHOLE tile set, mirroring
        // tile id stability — two terrain sets must never mint colliding terrain ids.
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setA = tileSet.AddTerrainSet("A", TerrainMatchMode.CornersAndSides);
        var setB = tileSet.AddTerrainSet("B", TerrainMatchMode.CornersAndSides);

        var earthId = tileSet.AddTerrain(setA, "Earth");
        var sandId = tileSet.AddTerrain(setB, "Sand");

        Assert.That(earthId, Is.Not.EqualTo(sandId));
    }

    [Test]
    public void AddTerrain_ToUnknownTerrainSet_ReturnsMinusOne()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.That(tileSet.AddTerrain(999, "Earth"), Is.EqualTo(-1));
    }

    [Test]
    public void RemoveTerrainSet_ThenAddTerrainSet_NeverReusesTheRemovedId()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var first = tileSet.AddTerrainSet("A", TerrainMatchMode.CornersAndSides);
        tileSet.RemoveTerrainSet(first);

        var second = tileSet.AddTerrainSet("B", TerrainMatchMode.CornersAndSides);

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void RemoveTerrainSet_DemotesMemberTiles_BackToPlain()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collides: false);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTilePeeringBits(tileId, TerrainPeering.All);

        tileSet.RemoveTerrainSet(setId);

        var tile = tileSet.Tiles[0];
        Assert.Multiple(() =>
        {
            Assert.That(tile.Terrain, Is.Null, "a tile whose terrain set was removed must not dangle-reference it.");
            Assert.That(tile.PeeringBits, Is.EqualTo(TerrainPeering.None), "peering bits are meaningless without a terrain — cleared alongside.");
        });
    }

    [Test]
    public void RemoveTerrain_DemotesMemberTiles_BackToPlain()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collides: false);
        tileSet.SetTileTerrain(tileId, terrainId);

        tileSet.RemoveTerrain(setId, terrainId);

        Assert.That(tileSet.Tiles[0].Terrain, Is.Null);
    }

    [Test]
    public void SetTileTerrain_ToUndeclaredTerrainId_IsNoOp()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var tileId = tileSet.AddTile(Png("A"), collides: false);

        var happened = tileSet.SetTileTerrain(tileId, 999);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(tileSet.Tiles[0].Terrain, Is.Null);
        });
    }

    [Test]
    public void SetTileTerrain_ToNull_ClearsPeeringBitsToo()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collides: false);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTilePeeringBits(tileId, TerrainPeering.All);

        tileSet.SetTileTerrain(tileId, null);

        Assert.Multiple(() =>
        {
            Assert.That(tileSet.Tiles[0].Terrain, Is.Null);
            Assert.That(tileSet.Tiles[0].PeeringBits, Is.EqualTo(TerrainPeering.None));
        });
    }

    [Test]
    public void SetTilePeeringBits_OnAPlainTile_IsNoOp()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var tileId = tileSet.AddTile(Png("A"), collides: false);

        var happened = tileSet.SetTilePeeringBits(tileId, TerrainPeering.North);

        Assert.That(happened, Is.False, "peering bits are meaningless on a tile that is not a terrain member.");
    }

    [Test]
    public void AddTile_ThenRename_PreservesTerrainMembership()
    {
        // Regression guard mirroring the animation fields' preservation across the tile's every other
        // mutation (RenameTile/SetTileCollides/AddFrame/etc all thread Terrain/PeeringBits through).
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collides: false);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTilePeeringBits(tileId, TerrainPeering.All);

        tileSet.RenameTile(tileId, "Earth Interior");
        tileSet.SetTileCollides(tileId, true);

        Assert.Multiple(() =>
        {
            Assert.That(tileSet.Tiles[0].Terrain, Is.EqualTo(terrainId));
            Assert.That(tileSet.Tiles[0].PeeringBits, Is.EqualTo(TerrainPeering.All));
        });
    }

    // ----- TileSetEditSession: dirty tracking -----

    [Test]
    public void Session_AddTerrainSetAndTerrain_MarksDirty()
    {
        var session = new TileSetEditSession(EditableTileSet.CreateBlank("Untitled"));

        var setId = session.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        Assert.That(session.IsDirty, Is.True);

        session.MarkSaved();
        session.AddTerrain(setId, "Earth");
        Assert.That(session.IsDirty, Is.True);
    }

    [Test]
    public void Session_SetTilePeeringBits_RoundTripsThroughTheSession()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var session = new TileSetEditSession(tileSet);
        var setId = session.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = session.AddTerrain(setId, "Earth");
        var tileId = session.AddTile(Png("A"), collides: false);
        session.SetTileTerrain(tileId, terrainId);

        var happened = session.SetTilePeeringBits(tileId, TerrainPeering.North | TerrainPeering.South);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(tileSet.Tiles[0].PeeringBits, Is.EqualTo(TerrainPeering.North | TerrainPeering.South));
        });
    }

    // ----- LevelEditSession: terrain brush + two-channel invariant -----

    [Test]
    public void PaintTerrain_SetsTerrainAndClearsAnyConcreteTile()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out var terrainId));
        session.PaintCell(0, 1, 0, 1); // paint a concrete tile first

        var change = session.PaintTerrain(0, 1, 0, terrainId);

        Assert.Multiple(() =>
        {
            Assert.That(change, Is.EqualTo(new CellChange(0, 1, 0, LayerDefinition.EmptyCell)), "a terrain paint reports the concrete channel going empty.");
            Assert.That(session.Level.Layers[0].Cells[1], Is.EqualTo(LayerDefinition.EmptyCell), "the two-channel invariant: painting terrain clears the concrete cell.");
            Assert.That(session.Level.Layers[0].Terrain[1], Is.EqualTo(terrainId));
        });
    }

    [Test]
    public void PaintCell_OnATerrainPaintedCell_ClearsTheTerrain()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out var terrainId));
        session.PaintTerrain(0, 1, 0, terrainId);

        session.PaintCell(0, 1, 0, 1);

        Assert.Multiple(() =>
        {
            Assert.That(session.Level.Layers[0].Cells[1], Is.EqualTo(1));
            Assert.That(session.Level.Layers[0].Terrain[1], Is.EqualTo(LayerDefinition.EmptyCell), "the two-channel invariant: painting a concrete tile clears any terrain paint.");
        });
    }

    [Test]
    public void EraseCell_ClearsBothTheConcreteTileAndAnyTerrainPaint()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out var terrainId));
        session.PaintTerrain(0, 1, 0, terrainId);

        session.EraseCell(0, 1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(session.Level.Layers[0].Cells[1], Is.EqualTo(LayerDefinition.EmptyCell));
            Assert.That(session.Level.Layers[0].Terrain[1], Is.EqualTo(LayerDefinition.EmptyCell));
        });
    }

    [Test]
    public void PaintTerrain_WhenCellAlreadyHoldsThatTerrain_IsNoOp()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out var terrainId));
        session.PaintTerrain(0, 1, 0, terrainId);

        var second = session.PaintTerrain(0, 1, 0, terrainId);

        Assert.That(second, Is.Null);
    }

    [Test]
    public void PaintTerrain_WithUndeclaredTerrainId_Throws()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out _));
        Assert.Throws<ArgumentException>(() => session.PaintTerrain(0, 1, 0, 999));
    }

    [Test]
    public void PaintTerrain_OutOfBounds_ReturnsNull()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out var terrainId));
        Assert.That(session.PaintTerrain(0, Width, 0, terrainId), Is.Null);
        Assert.That(session.IsDirty, Is.False);
    }

    [Test]
    public void EraseTerrain_ClearsTerrainOnly()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out var terrainId));
        session.PaintTerrain(0, 1, 0, terrainId);

        var change = session.EraseTerrain(0, 1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(change, Is.EqualTo(new CellChange(0, 1, 0, LayerDefinition.EmptyCell)));
            Assert.That(session.Level.Layers[0].Terrain[1], Is.EqualTo(LayerDefinition.EmptyCell));
        });
    }

    [Test]
    public void PaintTerrain_ThenUndo_RestoresThePreviousConcreteAndTerrainState()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out var terrainId));
        session.PaintCell(0, 1, 0, 1); // concrete tile first

        session.PaintTerrain(0, 1, 0, terrainId); // overwrites it with terrain paint
        var undo = session.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(undo, Is.EqualTo(new CellChange(0, 1, 0, 1)), "undo restores the concrete tile that was there before the terrain paint.");
            Assert.That(session.Level.Layers[0].Cells[1], Is.EqualTo(1));
            Assert.That(session.Level.Layers[0].Terrain[1], Is.EqualTo(LayerDefinition.EmptyCell));
        });
    }

    [Test]
    public void PaintCell_OverATerrainPaintedCell_ThenUndo_RestoresTheTerrainPaint()
    {
        var session = new LevelEditSession(SampleLevelWithTerrain(out var terrainId));
        session.PaintTerrain(0, 1, 0, terrainId);

        session.PaintCell(0, 1, 0, 1); // overwrites the terrain paint with a concrete tile
        var undo = session.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(undo, Is.EqualTo(new CellChange(0, 1, 0, LayerDefinition.EmptyCell)));
            Assert.That(session.Level.Layers[0].Cells[1], Is.EqualTo(LayerDefinition.EmptyCell));
            Assert.That(session.Level.Layers[0].Terrain[1], Is.EqualTo(terrainId), "undo restores the terrain paint that was there before the concrete overwrite.");
        });
    }

    [Test]
    public void WouldDropPaintedCells_DetectsATerrainPaintedCellBeingCropped()
    {
        // Design #7580 §7: a terrain-painted cell's Cells entry is EmptyCell, so the resize-confirm query
        // must check the terrain channel too, or a shrink could silently crop painted terrain.
        var level = SampleLevelWithTerrain(out var terrainId);
        var session = new LevelEditSession(level);
        session.PaintTerrain(0, Width - 1, 0, terrainId); // paint the rightmost column, about to be cropped

        Assert.That(level.WouldDropPaintedCells(Width - 1, Height), Is.True);
    }

    // ----- EditableLevelSnapshot: terrain data flows through (the P2-bug-class guard) -----

    [Test]
    public void ToResolvedLevel_CarriesTerrainSetsAndTileTerrains()
    {
        var level = SampleLevelWithTerrain(out var terrainId);

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TerrainSets, Has.Count.EqualTo(1));
            Assert.That(resolved.TerrainSets[0].Terrains[0].Id, Is.EqualTo(terrainId));
            Assert.That(resolved.TileTerrains, Contains.Key(2), "tile id 2 (EarthPath) is the terrain member in SampleLevelWithTerrain.");
            Assert.That(resolved.TileTerrains[2].TerrainId, Is.EqualTo(terrainId));
            Assert.That(resolved.TileTerrains[2].PeeringBits, Is.EqualTo(TerrainPeering.All));
        });
    }

    [Test]
    public void ToResolvedLevel_CarriesTheLayerTerrainChannel()
    {
        var level = SampleLevelWithTerrain(out var terrainId);
        var session = new LevelEditSession(level);
        session.PaintTerrain(0, 1, 0, terrainId);

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.That(resolved.Layers[0].Terrain, Is.EqualTo(new[] { LayerDefinition.EmptyCell, terrainId, LayerDefinition.EmptyCell }));
    }

    [Test]
    public void ToResolvedLevel_WithNoTerrainSetsBound_HasEmptyTerrainSetsAndTileTerrains()
    {
        var level = EditableLevel.CreateBlank("Untitled", TileSize, Width, Height, ResourceReference.ToSelf(TileSetPath), Palette());

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.TerrainSets, Is.Empty);
            Assert.That(resolved.TileTerrains, Is.Empty);
        });
    }

    // ----- Package round-trip -----

    [Test]
    public void TileSet_TerrainSetsAndMembership_RoundTrip_ThroughReaderAndWriter()
    {
        var tileSet = EditableTileSet.CreateBlank("Ground Set");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.Sides);
        var terrainId = tileSet.AddTerrain(setId, "Earth", "#8a5c34");
        var tileId = tileSet.AddTile(Png("EARTH"), collides: true);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTilePeeringBits(tileId, TerrainPeering.All);

        var session = new TileSetEditSession(tileSet);
        session.AttachAsNewResource(Array.Empty<ResourceEntry>());

        var package = BuildPackageFrom(session.BuildContributions());
        var restored = EditableTileSetReader.FromPackage(package);

        Assert.Multiple(() =>
        {
            Assert.That(restored.TerrainSets, Has.Count.EqualTo(1));
            Assert.That(restored.TerrainSets[0].Name, Is.EqualTo("Ground"));
            Assert.That(restored.TerrainSets[0].MatchingMode, Is.EqualTo(TerrainMatchMode.Sides));
            Assert.That(restored.TerrainSets[0].Terrains[0].Name, Is.EqualTo("Earth"));
            Assert.That(restored.TerrainSets[0].Terrains[0].Color, Is.EqualTo("#8a5c34"));
            var restoredTile = restored.Tiles.First(t => t.Id == tileId);
            Assert.That(restoredTile.Terrain, Is.EqualTo(terrainId));
            Assert.That(restoredTile.PeeringBits, Is.EqualTo(TerrainPeering.All));
        });
    }

    [Test]
    public void Level_TerrainChannel_RoundTrips_ThroughReaderAndWriter()
    {
        var level = SampleLevelWithTerrain(out var terrainId);
        var levelSession = new LevelEditSession(level);
        levelSession.PaintTerrain(0, 1, 0, terrainId);

        var tileSetSession = new TileSetEditSession(TileSetFor(level));
        tileSetSession.AttachAsNewResource(Array.Empty<ResourceEntry>());
        levelSession.Level.BindTileSet(ResourceReference.ToSelf(tileSetSession.TileSet.TileSetPath), tileSetSession.TileSet.Tiles, tileSetSession.TileSet.TerrainSets);
        levelSession.AttachAsNewResource(Array.Empty<ResourceEntry>());

        var contributions = levelSession.BuildContributions().Concat(tileSetSession.BuildContributions()).ToList();
        var package = BuildPackageFrom(contributions);
        var restored = EditableLevelReader.FromPackage(package);

        Assert.That(restored.Layers[0].Terrain, Is.EqualTo(new[] { LayerDefinition.EmptyCell, terrainId, LayerDefinition.EmptyCell }));
    }

    [Test]
    public void Level_WithNoTerrainEverPainted_RoundTrips_WithAnAllEmptyTerrainChannel()
    {
        // LevelMergeWriter omits the terrain array from the JSON when nothing is painted (mirrors Frames'
        // omit-when-default convention) — this pins that the READ side still comes back fully populated
        // (EditableLayer's own invariant), i.e. omission is transparent to a round trip.
        var level = SampleLevel();
        var levelSession = new LevelEditSession(level);
        var tileSetSession = new TileSetEditSession(TileSetFor(level));
        tileSetSession.AttachAsNewResource(Array.Empty<ResourceEntry>());
        levelSession.Level.BindTileSet(ResourceReference.ToSelf(tileSetSession.TileSet.TileSetPath), tileSetSession.TileSet.Tiles);
        levelSession.AttachAsNewResource(Array.Empty<ResourceEntry>());

        var contributions = levelSession.BuildContributions().Concat(tileSetSession.BuildContributions()).ToList();
        var package = BuildPackageFrom(contributions);
        var restored = EditableLevelReader.FromPackage(package);

        Assert.That(restored.Layers[0].Terrain, Is.All.EqualTo(LayerDefinition.EmptyCell));
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
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            Palette(), new[] { layer });
    }

    // A level whose bound tile set declares one terrain set ("Ground") with one terrain ("Earth", id
    // returned via terrainId) and one plain tile (id 1, grass) plus one terrain-member tile (id 2, earth,
    // full peering bits) — the shared fixture every terrain-brush/snapshot/round-trip test above builds on.
    private static EditableLevel SampleLevelWithTerrain(out int terrainId)
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        terrainId = tileSet.AddTerrain(setId, "Earth", "#8a5c34");
        tileSet.AddTile(Encoding.UTF8.GetBytes("GRASS-PNG"), collides: true, name: "grass"); // id 1
        var earthTileId = tileSet.AddTile(Encoding.UTF8.GetBytes("EARTH-PNG"), collides: true, name: "earth"); // id 2
        tileSet.SetTileTerrain(earthTileId, terrainId);
        tileSet.SetTilePeeringBits(earthTileId, TerrainPeering.All);

        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        return new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            tileSet.Tiles, new[] { layer }, terrainSets: tileSet.TerrainSets);
    }

    // Rebuilds a blank EditableTileSet with the same tiles/terrain sets a level's palette cache is
    // currently showing — the round-trip tests need a real TileSetEditSession (not just the level's cache)
    // to actually persist the tile set as its own resource.
    private static EditableTileSet TileSetFor(EditableLevel level) =>
        new("Untitled", TileSetResourcePaths.TileSetPath("untitled"), level.Tiles, isAttached: false, terrainSets: level.TerrainSets);

    private static Package BuildPackageFrom(IReadOnlyList<PendingResource> contributions)
    {
        var builder = new PackageBuilder().WithName("Demo Pack");
        foreach (var resource in contributions)
            builder.AddResource(resource.Kind, resource.Path, resource.Payload, resource.MediaType);

        var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;
        return PackageReader.Open(buffer);
    }
}
