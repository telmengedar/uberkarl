using System.Text;
using NUnit.Framework;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers the per-terrain default/fallback tile authoring model (DiVoid #7638, follow-up to Phase 3's
/// terrain auto-tiling): <see cref="EditableTileSet.SetTerrainDefaultTile"/>'s membership guard,
/// <see cref="TileSetEditSession"/>'s dirty tracking, self-consistency (a terrain's
/// <see cref="EditableTerrain.DefaultTile"/> never dangles after the tile it points at is removed or
/// reassigned elsewhere), preservation across a terrain's other mutations (rename/recolour — mirrors
/// <c>TerrainAuthoringTests.AddTile_ThenRename_PreservesTerrainMembership</c>'s regression-guard shape),
/// the <see cref="EditableLevelSnapshot"/> projection carrying it into <see cref="ResolvedLevel"/> (the P2
/// bug class this project guards against for every tile/terrain capability), and the package round-trip.
/// Engine-agnostic — no Godot; the actual "Godot left this cell empty, fill it with the default" mechanism
/// is verified in-engine (see <c>Uberkarl.Content.Tests.TerrainDefaultTileTests</c>'s doc comment).
/// </summary>
[TestFixture]
public sealed class TerrainDefaultTileAuthoringTests
{
    private static byte[] Png(string marker) => Encoding.UTF8.GetBytes(marker);

    // ----- EditableTileSet: SetTerrainDefaultTile -----

    [Test]
    public void SetTerrainDefaultTile_ToAMemberTile_SetsIt_AndIsListed()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(tileId, terrainId);

        var happened = tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(tileSet.TerrainSets[0].Terrains[0].DefaultTile, Is.EqualTo(tileId));
        });
    }

    [Test]
    public void SetTerrainDefaultTile_ToATileNotAMemberOfThisTerrain_IsNoOp()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var plainTileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None); // never assigned to any terrain

        var happened = tileSet.SetTerrainDefaultTile(setId, terrainId, plainTileId);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.False);
            Assert.That(tileSet.TerrainSets[0].Terrains[0].DefaultTile, Is.Null);
        });
    }

    [Test]
    public void SetTerrainDefaultTile_ToATileMemberOfADifferentTerrain_IsNoOp()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var earthId = tileSet.AddTerrain(setId, "Earth");
        var sandId = tileSet.AddTerrain(setId, "Sand");
        var sandTileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(sandTileId, sandId);

        var happened = tileSet.SetTerrainDefaultTile(setId, earthId, sandTileId);

        Assert.That(happened, Is.False);
    }

    [Test]
    public void SetTerrainDefaultTile_OnUnknownTerrain_IsNoOp()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        Assert.That(tileSet.SetTerrainDefaultTile(999, 999, null), Is.False);
    }

    [Test]
    public void SetTerrainDefaultTile_ToNull_ClearsIt()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        var happened = tileSet.SetTerrainDefaultTile(setId, terrainId, null);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(tileSet.TerrainSets[0].Terrains[0].DefaultTile, Is.Null);
        });
    }

    [Test]
    public void SetTerrainDefaultTile_ToItsCurrentValue_IsNoOp()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        var happened = tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        Assert.That(happened, Is.False);
    }

    // ----- Self-consistency: a dangling default tile is cleared, not left dangling -----

    [Test]
    public void RemoveTile_ClearsAnyTerrainDefaultTileThatPointedAtIt()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        tileSet.RemoveTile(tileId);

        Assert.That(tileSet.TerrainSets[0].Terrains[0].DefaultTile, Is.Null,
            "a terrain's default tile must not dangle-reference a removed tile.");
    }

    [Test]
    public void SetTileTerrain_ReassigningTheDefaultTileAway_ClearsTheTerrainsDefaultTile()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var earthId = tileSet.AddTerrain(setId, "Earth");
        var sandId = tileSet.AddTerrain(setId, "Sand");
        var tileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(tileId, earthId);
        tileSet.SetTerrainDefaultTile(setId, earthId, tileId);

        tileSet.SetTileTerrain(tileId, sandId); // reassign the tile to a different terrain

        var earth = tileSet.TerrainSets[0].Terrains.First(terrain => terrain.Id == earthId);
        Assert.That(earth.DefaultTile, Is.Null,
            "the tile is no longer a member of Earth, so Earth's default tile reference must be cleared.");
    }

    [Test]
    public void SetTileTerrain_DemotingTheDefaultTileToPlain_ClearsTheTerrainsDefaultTile()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        tileSet.SetTileTerrain(tileId, null); // demote back to a plain tile

        Assert.That(tileSet.TerrainSets[0].Terrains[0].DefaultTile, Is.Null);
    }

    // ----- Preservation across other terrain mutations -----

    [Test]
    public void RenameTerrain_PreservesItsDefaultTile()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        tileSet.RenameTerrain(setId, terrainId, "Dirt");

        Assert.That(tileSet.TerrainSets[0].Terrains[0].DefaultTile, Is.EqualTo(tileId));
    }

    [Test]
    public void SetTerrainColor_PreservesItsDefaultTile()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = tileSet.AddTerrain(setId, "Earth");
        var tileId = tileSet.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        tileSet.SetTerrainColor(setId, terrainId, "#123456");

        Assert.That(tileSet.TerrainSets[0].Terrains[0].DefaultTile, Is.EqualTo(tileId));
    }

    // ----- TileSetEditSession: dirty tracking -----

    [Test]
    public void Session_SetTerrainDefaultTile_MarksDirty_AndRoundTripsThroughTheSession()
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var session = new TileSetEditSession(tileSet);
        var setId = session.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        var terrainId = session.AddTerrain(setId, "Earth");
        var tileId = session.AddTile(Png("A"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.None);
        session.SetTileTerrain(tileId, terrainId);
        session.MarkSaved();

        var happened = session.SetTerrainDefaultTile(setId, terrainId, tileId);

        Assert.Multiple(() =>
        {
            Assert.That(happened, Is.True);
            Assert.That(session.IsDirty, Is.True);
            Assert.That(tileSet.TerrainSets[0].Terrains[0].DefaultTile, Is.EqualTo(tileId));
        });
    }

    // ----- EditableLevelSnapshot: default tile id flows through (the P2-bug-class guard) -----

    [Test]
    public void ToResolvedLevel_CarriesTheTerrainsDefaultTileId()
    {
        var level = SampleLevelWithTerrainAndDefaultTile(out _, out var defaultTileId);

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.That(resolved.TerrainSets[0].Terrains[0].DefaultTileId, Is.EqualTo(defaultTileId));
    }

    [Test]
    public void ToResolvedLevel_WithNoDefaultTileDeclared_HasNullDefaultTileId()
    {
        var level = SampleLevelWithTerrainAndDefaultTile(out _, out _, declareDefault: false);

        var resolved = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.That(resolved.TerrainSets[0].Terrains[0].DefaultTileId, Is.Null);
    }

    // ----- Package round-trip -----

    [Test]
    public void TileSet_TerrainDefaultTile_RoundTrips_ThroughReaderAndWriter()
    {
        var tileSet = EditableTileSet.CreateBlank("Ground Set");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.Sides);
        var terrainId = tileSet.AddTerrain(setId, "Earth", "#8a5c34");
        var tileId = tileSet.AddTile(Png("EARTH"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.Full);
        tileSet.SetTileTerrain(tileId, terrainId);
        tileSet.SetTilePeeringBits(tileId, TerrainPeering.All);
        tileSet.SetTerrainDefaultTile(setId, terrainId, tileId);

        var session = new TileSetEditSession(tileSet);
        session.AttachAsNewResource(Array.Empty<ResourceEntry>());

        var package = BuildPackageFrom(session.BuildContributions());
        var restored = EditableTileSetReader.FromPackage(package);

        Assert.That(restored.TerrainSets[0].Terrains[0].DefaultTile, Is.EqualTo(tileId));
    }

    [Test]
    public void TileSet_WithNoTerrainDefaultTileDeclared_RoundTrips_AsNull()
    {
        var tileSet = EditableTileSet.CreateBlank("Ground Set");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.Sides);
        tileSet.AddTerrain(setId, "Earth", "#8a5c34");

        var session = new TileSetEditSession(tileSet);
        session.AttachAsNewResource(Array.Empty<ResourceEntry>());

        var package = BuildPackageFrom(session.BuildContributions());
        var restored = EditableTileSetReader.FromPackage(package);

        Assert.That(restored.TerrainSets[0].Terrains[0].DefaultTile, Is.Null);
    }

    // ----- helpers -----

    private const int TileSize = 16;
    private const int Width = 3;
    private const int Height = 1;
    private static readonly ResourcePath LevelPath = ResourcePath.Create("levels/demo.json");
    private static readonly ResourcePath TileSetPath = ResourcePath.Create("tileset.json");

    // A level whose bound tile set declares one terrain set ("Ground") with one terrain ("Earth", id
    // returned via terrainId) with one plain tile (id 1, grass) and one terrain-member tile (id 2, earth,
    // full peering bits) that is also — unless declareDefault is false — the terrain's DefaultTile.
    private static EditableLevel SampleLevelWithTerrainAndDefaultTile(out int terrainId, out int defaultTileId, bool declareDefault = true)
    {
        var tileSet = EditableTileSet.CreateBlank("Untitled");
        var setId = tileSet.AddTerrainSet("Ground", TerrainMatchMode.CornersAndSides);
        terrainId = tileSet.AddTerrain(setId, "Earth", "#8a5c34");
        tileSet.AddTile(Encoding.UTF8.GetBytes("GRASS-PNG"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.Full, name: "grass"); // id 1
        var earthTileId = tileSet.AddTile(Encoding.UTF8.GetBytes("EARTH-PNG"), collisionShape: Uberkarl.Content.CollisionShapeDefinition.Full, name: "earth"); // id 2
        tileSet.SetTileTerrain(earthTileId, terrainId);
        tileSet.SetTilePeeringBits(earthTileId, TerrainPeering.All);
        if (declareDefault)
            tileSet.SetTerrainDefaultTile(setId, terrainId, earthTileId);
        defaultTileId = earthTileId;

        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);
        return new EditableLevel(
            "Sample", LevelPath, ResourceReference.ToSelf(TileSetPath),
            TileSize, Width, Height, backgroundColor: null,
            new Dictionary<string, GridPosition>(), defaultSpawn: null,
            tileSet.Tiles, new[] { layer }, tileSet.Scripts, terrainSets: tileSet.TerrainSets);
    }

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
