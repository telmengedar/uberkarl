using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// The editor's authoring model of a <b>standalone, shared</b> tile set (DiVoid #7551 Phase 1a/1b, design
/// #7580) — the counterpart to <see cref="EditableLevel"/> but for a tile set resource. Before this, a
/// tileset was fabricated fresh on every level save from <c>EditableLevel.Tiles</c> and namespaced under
/// the LEVEL's own slug (<c>LevelMergeWriter</c>'s old behaviour) — every level saved its own private copy
/// even when identical, the redundancy Toni flagged. Now a tile set is its own resource with its own
/// identity/path/lifecycle: a level <b>references</b> it (<see cref="EditableLevel.TileSetReference"/>)
/// instead of owning it, and many levels can bind the very same tile set resource.
///
/// Mirrors <see cref="EditableLevel"/>'s attach/namespace/round-trip shape exactly (same
/// <see cref="IsAttached"/> semantics, same slug-derived path scheme via
/// <see cref="TileSetResourcePaths"/>) so the two models read as one learnable pattern.
///
/// <b>Tile id stability</b> (design #7580 §11 risk): ids are never reused, even after
/// <see cref="RemoveTile"/> — <see cref="AddTile"/> always mints a fresh id past the highest ever issued,
/// tracked by <c>nextTileId</c> independently of the current tile count. This is what keeps a stale
/// reference (e.g. a level layer cell painted with a since-removed id) a detectable dangling reference
/// rather than silently aliasing onto a different, later-added tile.
/// </summary>
public sealed class EditableTileSet
{
    private readonly List<EditableTile> tiles;
    private readonly List<EditableTerrainSet> terrainSets;
    private int nextTileId;
    private int nextTerrainSetId;
    private int nextTerrainId;

    public EditableTileSet(
        string name,
        ResourcePath tileSetPath,
        IReadOnlyList<EditableTile> tiles,
        bool isAttached = false,
        int? nextTileId = null,
        IReadOnlyList<EditableTerrainSet>? terrainSets = null,
        int? nextTerrainSetId = null,
        int? nextTerrainId = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TileSetPath = tileSetPath;
        this.tiles = new List<EditableTile>(tiles ?? throw new ArgumentNullException(nameof(tiles)));
        IsAttached = isAttached;
        this.nextTileId = nextTileId ?? (this.tiles.Count == 0 ? 1 : this.tiles.Max(tile => tile.Id) + 1);

        this.terrainSets = new List<EditableTerrainSet>(terrainSets ?? Array.Empty<EditableTerrainSet>());
        this.nextTerrainSetId = nextTerrainSetId ?? (this.terrainSets.Count == 0 ? 1 : this.terrainSets.Max(set => set.Id) + 1);
        var allTerrainIds = this.terrainSets.SelectMany(set => set.Terrains).Select(terrain => terrain.Id).ToList();
        this.nextTerrainId = nextTerrainId ?? (allTerrainIds.Count == 0 ? 1 : allTerrainIds.Max() + 1);
    }

    public string Name { get; private set; }

    /// <summary>This tile set's own in-package path (tilesets/&lt;slug&gt;.json once attached). Fixed once <see cref="IsAttached"/> — see <see cref="Attach"/>.</summary>
    public ResourcePath TileSetPath { get; private set; }

    /// <summary>
    /// Whether this tile set already occupies a stable, namespaced resource slot in some package — true
    /// for one just loaded via <see cref="EditableTileSetReader"/>, or one that has completed at least one
    /// merge-save via <see cref="Attach"/>. False for a freshly <see cref="CreateBlank"/> tile set.
    /// </summary>
    public bool IsAttached { get; private set; }

    public IReadOnlyList<EditableTile> Tiles => tiles;

    /// <summary>The tile set's logical terrain sets (DiVoid #7551 Phase 3, design #7580).</summary>
    public IReadOnlyList<EditableTerrainSet> TerrainSets => terrainSets;

    /// <summary>True when <paramref name="tileId"/> names a declared tile.</summary>
    public bool Contains(int tileId)
    {
        foreach (var tile in tiles)
        {
            if (tile.Id == tileId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds a new simple tile (DiVoid #7551 Phase 1b — no animation/terrain semantics yet) with
    /// <paramref name="graphic"/> as its imported graphic bytes. Mints a fresh, never-before-used id and a
    /// provisional graphic path derived from this tile set's current name — remapped for real by
    /// <see cref="Attach"/> once the tile set is actually saved, exactly like a freshly-created level's
    /// palette. Returns the new tile's id.
    /// </summary>
    public int AddTile(byte[] graphic, bool collides, string? name = null)
    {
        if (graphic is null || graphic.Length == 0)
            throw new ArgumentException("Tile graphic must not be empty.", nameof(graphic));

        var id = nextTileId++;
        var provisionalSlug = TileSetResourcePaths.Slugify(Name);
        var path = TileSetResourcePaths.GraphicPath(provisionalSlug, id);
        tiles.Add(new EditableTile(id, path, graphic, collides, name));
        return id;
    }

    /// <summary>
    /// Removes the tile with <paramref name="id"/>. No cross-check against any level's painted cells here
    /// (design #7580 §11 — full reference-guarded removal is future work; the loader's typed validation is
    /// the defensive backstop for a dangling reference this leaves behind). Returns <c>false</c> (no-op)
    /// when <paramref name="id"/> is not a declared tile.
    /// </summary>
    public bool RemoveTile(int id)
    {
        var index = tiles.FindIndex(tile => tile.Id == id);
        if (index < 0)
            return false;

        tiles.RemoveAt(index);
        return true;
    }

    /// <summary>Renames the tile at <paramref name="id"/> (DiVoid #7513 — the on-screen keyboard). Returns <c>false</c> (no-op) when the tile does not exist or the name is unchanged (ordinal comparison, blank normalized to <c>null</c>).</summary>
    public bool RenameTile(int id, string? name)
    {
        var index = tiles.FindIndex(tile => tile.Id == id);
        if (index < 0)
            return false;

        var normalized = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var current = tiles[index];
        if (string.Equals(current.Name, normalized, StringComparison.Ordinal))
            return false;

        tiles[index] = new EditableTile(current.Id, current.GraphicPath, current.Graphic, current.Collides, normalized, current.Frames, current.AnimationSpeed, current.Terrain, current.PeeringBits);
        return true;
    }

    /// <summary>Sets whether the tile at <paramref name="id"/> is solid. Returns <c>false</c> (no-op) when the tile does not exist or the flag is unchanged.</summary>
    public bool SetTileCollides(int id, bool collides)
    {
        var index = tiles.FindIndex(tile => tile.Id == id);
        if (index < 0)
            return false;

        var current = tiles[index];
        if (current.Collides == collides)
            return false;

        tiles[index] = new EditableTile(current.Id, current.GraphicPath, current.Graphic, collides, current.Name, current.Frames, current.AnimationSpeed, current.Terrain, current.PeeringBits);
        return true;
    }

    /// <summary>
    /// Appends a new animation frame to the tile at <paramref name="id"/> — the frame goes on the END of
    /// <see cref="EditableTile.Frames"/> (frame index <c>Frames.Count + 1</c> overall, since
    /// <see cref="EditableTile.Graphic"/> is always frame 0). A tile with zero frames going to one is the
    /// simple→animated structural transition (design #7580 §7/§10 — no separate flag). Mints a provisional
    /// path from this tile set's current name, remapped for real by <see cref="Attach"/>, exactly like
    /// <see cref="AddTile"/>. Returns <c>false</c> (no-op) when <paramref name="id"/> is not a declared tile.
    /// </summary>
    public bool AddFrame(int id, byte[] graphic)
    {
        if (graphic is null || graphic.Length == 0)
            throw new ArgumentException("Frame graphic must not be empty.", nameof(graphic));

        var index = tiles.FindIndex(tile => tile.Id == id);
        if (index < 0)
            return false;

        var current = tiles[index];
        var overallFrameNumber = current.Frames.Count + 2; // overall frame 1 is current.Graphic
        var provisionalSlug = TileSetResourcePaths.Slugify(Name);
        var path = TileSetResourcePaths.FramePath(provisionalSlug, id, overallFrameNumber);
        var frames = new List<EditableTileFrame>(current.Frames) { new EditableTileFrame(path, graphic) };
        tiles[index] = new EditableTile(current.Id, current.GraphicPath, current.Graphic, current.Collides, current.Name, frames, current.AnimationSpeed, current.Terrain, current.PeeringBits);
        return true;
    }

    /// <summary>
    /// Removes the animation frame at <paramref name="frameIndex"/> (0-based into <see cref="EditableTile.Frames"/>
    /// — i.e. overall animation frame <c>frameIndex + 2</c>; <see cref="EditableTile.Graphic"/>, overall
    /// frame 1, is never removable this way). Going from one frame back to zero is the animated→simple
    /// structural transition (design #7580 §7/§10, task-scoped: "removing frames back to one ⇒ simple tile
    /// again"). Returns <c>false</c> (no-op) when <paramref name="id"/> is not a declared tile or
    /// <paramref name="frameIndex"/> is out of range.
    /// </summary>
    public bool RemoveFrame(int id, int frameIndex)
    {
        var index = tiles.FindIndex(tile => tile.Id == id);
        if (index < 0)
            return false;

        var current = tiles[index];
        if (frameIndex < 0 || frameIndex >= current.Frames.Count)
            return false;

        var frames = new List<EditableTileFrame>(current.Frames);
        frames.RemoveAt(frameIndex);
        tiles[index] = new EditableTile(current.Id, current.GraphicPath, current.Graphic, current.Collides, current.Name, frames, current.AnimationSpeed, current.Terrain, current.PeeringBits);
        return true;
    }

    /// <summary>
    /// Sets the animation speed (frames per second) of the tile at <paramref name="id"/>. Meaningful only
    /// once the tile <see cref="EditableTile.IsAnimated"/>, but settable regardless — an author may set the
    /// speed before or after adding the second frame. Returns <c>false</c> (no-op) when the tile does not
    /// exist or the speed is unchanged.
    /// </summary>
    public bool SetAnimationSpeed(int id, double speed)
    {
        if (speed <= 0)
            throw new ArgumentException("Animation speed must be positive.", nameof(speed));

        var index = tiles.FindIndex(tile => tile.Id == id);
        if (index < 0)
            return false;

        var current = tiles[index];
        if (current.AnimationSpeed == speed)
            return false;

        tiles[index] = new EditableTile(current.Id, current.GraphicPath, current.Graphic, current.Collides, current.Name, current.Frames, speed, current.Terrain, current.PeeringBits);
        return true;
    }

    /// <summary>Renames this tile set's own display name. Returns <c>false</c> (no-op) when unchanged (ordinal comparison) — mirrors <see cref="EditableLevel.Rename"/>.</summary>
    public bool Rename(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Tile set name must not be empty.", nameof(name));
        if (string.Equals(Name, name, StringComparison.Ordinal))
            return false;

        Name = name;
        return true;
    }

    /// <summary>
    /// Establishes (or re-establishes) this tile set's namespaced resource slot in a package: derives
    /// <see cref="TileSetPath"/> from <paramref name="slug"/>, or reuses <paramref name="overwriteTileSetPath"/>
    /// verbatim, and remaps every tile's graphic path to <c>graphics/&lt;slug&gt;/&lt;tileId&gt;.png</c> —
    /// mirrors <see cref="EditableLevel.Attach"/> exactly. Marks <see cref="IsAttached"/> so a later plain
    /// save reuses these exact paths.
    /// </summary>
    public void Attach(string slug, ResourcePath? overwriteTileSetPath)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug must not be empty.", nameof(slug));

        TileSetPath = overwriteTileSetPath ?? TileSetResourcePaths.TileSetPath(slug);
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            var frames = tile.Frames.Count == 0
                ? tile.Frames
                : tile.Frames
                    .Select((frame, frameIndex) => new EditableTileFrame(TileSetResourcePaths.FramePath(slug, tile.Id, frameIndex + 2), frame.Graphic))
                    .ToArray();
            tiles[i] = new EditableTile(tile.Id, TileSetResourcePaths.GraphicPath(slug, tile.Id), tile.Graphic, tile.Collides, tile.Name, frames, tile.AnimationSpeed, tile.Terrain, tile.PeeringBits);
        }

        IsAttached = true;
    }

    // ----- terrain authoring (DiVoid #7551 Phase 3, design #7580 §7/§14) -----

    /// <summary>Adds a new, empty terrain set. Mints a fresh, never-before-used id (mirrors <see cref="AddTile"/>). Returns the new terrain set's id.</summary>
    public int AddTerrainSet(string name, Content.TerrainMatchMode matchingMode)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Terrain set name must not be empty.", nameof(name));

        var id = nextTerrainSetId++;
        terrainSets.Add(new EditableTerrainSet(id, name.Trim(), matchingMode, Array.Empty<EditableTerrain>()));
        return id;
    }

    /// <summary>
    /// Removes the terrain set with <paramref name="id"/> and every terrain belonging to it. Any tile
    /// currently a member of one of those terrains is demoted back to a plain tile (its <see cref="EditableTile.Terrain"/>
    /// cleared) rather than left dangling — mirrors <see cref="RemoveTile"/>'s "no cross-check, loader is the
    /// defensive backstop" stance for LEVEL references, but a tile's own membership is this tile set's own
    /// data, so it is kept internally consistent here. Returns <c>false</c> (no-op) when not found.
    /// </summary>
    public bool RemoveTerrainSet(int id)
    {
        var index = terrainSets.FindIndex(set => set.Id == id);
        if (index < 0)
            return false;

        var removedTerrainIds = new HashSet<int>(terrainSets[index].Terrains.Select(terrain => terrain.Id));
        terrainSets.RemoveAt(index);
        DemoteTilesOfTerrains(removedTerrainIds);
        return true;
    }

    /// <summary>Renames the terrain set with <paramref name="id"/>. Returns <c>false</c> (no-op) when not found or unchanged.</summary>
    public bool RenameTerrainSet(int id, string name)
    {
        var index = terrainSets.FindIndex(set => set.Id == id);
        if (index < 0 || string.IsNullOrWhiteSpace(name))
            return false;

        var current = terrainSets[index];
        var trimmed = name.Trim();
        if (string.Equals(current.Name, trimmed, StringComparison.Ordinal))
            return false;

        terrainSets[index] = new EditableTerrainSet(current.Id, trimmed, current.MatchingMode, current.Terrains);
        return true;
    }

    /// <summary>Sets the matching mode of the terrain set with <paramref name="id"/>. Returns <c>false</c> (no-op) when not found or unchanged.</summary>
    public bool SetTerrainSetMatchingMode(int id, Content.TerrainMatchMode matchingMode)
    {
        var index = terrainSets.FindIndex(set => set.Id == id);
        if (index < 0)
            return false;

        var current = terrainSets[index];
        if (current.MatchingMode == matchingMode)
            return false;

        terrainSets[index] = new EditableTerrainSet(current.Id, current.Name, matchingMode, current.Terrains);
        return true;
    }

    /// <summary>Adds a new terrain to the terrain set with <paramref name="terrainSetId"/>. Mints a fresh id UNIQUE ACROSS THE WHOLE TILE SET (not just this set — mirrors <see cref="TerrainDefinition.Id"/>'s stability). Returns the new terrain's id, or <c>-1</c> when the terrain set does not exist.</summary>
    public int AddTerrain(int terrainSetId, string name, string? color = null)
    {
        var index = terrainSets.FindIndex(set => set.Id == terrainSetId);
        if (index < 0)
            return -1;
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Terrain name must not be empty.", nameof(name));

        var id = nextTerrainId++;
        var current = terrainSets[index];
        var terrains = new List<EditableTerrain>(current.Terrains) { new EditableTerrain(id, name.Trim(), color) };
        terrainSets[index] = new EditableTerrainSet(current.Id, current.Name, current.MatchingMode, terrains);
        return id;
    }

    /// <summary>Removes the terrain with <paramref name="terrainId"/> from the terrain set with <paramref name="terrainSetId"/>, demoting any member tile back to plain. Returns <c>false</c> (no-op) when not found.</summary>
    public bool RemoveTerrain(int terrainSetId, int terrainId)
    {
        var index = terrainSets.FindIndex(set => set.Id == terrainSetId);
        if (index < 0)
            return false;

        var current = terrainSets[index];
        var terrainIndex = current.Terrains.ToList().FindIndex(terrain => terrain.Id == terrainId);
        if (terrainIndex < 0)
            return false;

        var terrains = new List<EditableTerrain>(current.Terrains);
        terrains.RemoveAt(terrainIndex);
        terrainSets[index] = new EditableTerrainSet(current.Id, current.Name, current.MatchingMode, terrains);
        DemoteTilesOfTerrains(new HashSet<int> { terrainId });
        return true;
    }

    /// <summary>Renames the terrain with <paramref name="terrainId"/> in the terrain set with <paramref name="terrainSetId"/>. Returns <c>false</c> (no-op) when not found or unchanged.</summary>
    public bool RenameTerrain(int terrainSetId, int terrainId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return ReplaceTerrain(terrainSetId, terrainId, terrain =>
            string.Equals(terrain.Name, name.Trim(), StringComparison.Ordinal)
                ? null
                : new EditableTerrain(terrain.Id, name.Trim(), terrain.Color));
    }

    /// <summary>Sets the author colour of the terrain with <paramref name="terrainId"/>. Returns <c>false</c> (no-op) when not found or unchanged.</summary>
    public bool SetTerrainColor(int terrainSetId, int terrainId, string? color)
        => ReplaceTerrain(terrainSetId, terrainId, terrain =>
            string.Equals(terrain.Color, color, StringComparison.OrdinalIgnoreCase)
                ? null
                : new EditableTerrain(terrain.Id, terrain.Name, color));

    private bool ReplaceTerrain(int terrainSetId, int terrainId, Func<EditableTerrain, EditableTerrain?> update)
    {
        var setIndex = terrainSets.FindIndex(set => set.Id == terrainSetId);
        if (setIndex < 0)
            return false;

        var current = terrainSets[setIndex];
        var terrains = current.Terrains.ToList();
        var terrainIndex = terrains.FindIndex(terrain => terrain.Id == terrainId);
        if (terrainIndex < 0)
            return false;

        var updated = update(terrains[terrainIndex]);
        if (updated is null)
            return false;

        terrains[terrainIndex] = updated;
        terrainSets[setIndex] = new EditableTerrainSet(current.Id, current.Name, current.MatchingMode, terrains);
        return true;
    }

    /// <summary>
    /// Assigns the tile with <paramref name="tileId"/> to terrain <paramref name="terrainId"/> — or, when
    /// <paramref name="terrainId"/> is <c>null</c>, demotes it back to a plain tile (clearing its peering
    /// bits too, since they are meaningless without a terrain). Returns <c>false</c> (no-op) when the tile
    /// does not exist, or <paramref name="terrainId"/> names an undeclared terrain.
    /// </summary>
    public bool SetTileTerrain(int tileId, int? terrainId)
    {
        var index = tiles.FindIndex(tile => tile.Id == tileId);
        if (index < 0)
            return false;
        if (terrainId is { } id && !terrainSets.Any(set => set.Terrains.Any(terrain => terrain.Id == id)))
            return false;

        var current = tiles[index];
        if (current.Terrain == terrainId)
            return false;

        var peering = terrainId is null ? Content.TerrainPeering.None : current.PeeringBits;
        tiles[index] = new EditableTile(current.Id, current.GraphicPath, current.Graphic, current.Collides, current.Name, current.Frames, current.AnimationSpeed, terrainId, peering);
        return true;
    }

    /// <summary>
    /// Sets the tile with <paramref name="tileId"/>'s peering bits wholesale (design #7580 §14 — the 3×3
    /// grid commits the whole bitmask each toggle/preset). No-op if the tile is not currently a terrain
    /// variant (<see cref="EditableTile.IsTerrainVariant"/> false) or the bits are unchanged.
    /// </summary>
    public bool SetTilePeeringBits(int tileId, Content.TerrainPeering peeringBits)
    {
        var index = tiles.FindIndex(tile => tile.Id == tileId);
        if (index < 0)
            return false;

        var current = tiles[index];
        if (!current.IsTerrainVariant || current.PeeringBits == peeringBits)
            return false;

        tiles[index] = new EditableTile(current.Id, current.GraphicPath, current.Graphic, current.Collides, current.Name, current.Frames, current.AnimationSpeed, current.Terrain, peeringBits);
        return true;
    }

    private void DemoteTilesOfTerrains(HashSet<int> removedTerrainIds)
    {
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile.Terrain is { } terrainId && removedTerrainIds.Contains(terrainId))
                tiles[i] = new EditableTile(tile.Id, tile.GraphicPath, tile.Graphic, tile.Collides, tile.Name, tile.Frames, tile.AnimationSpeed, null, Content.TerrainPeering.None);
        }
    }

    /// <summary>
    /// Creates an empty (or pre-seeded, via <paramref name="initialTiles"/>) tile set, unattached — mirrors
    /// <see cref="EditableLevel.CreateBlank"/>. A freshly-created level seeds its bound tile set from
    /// <c>DefaultPalette</c> this way so "New" still opens paintable, exactly as before this correction.
    /// </summary>
    public static EditableTileSet CreateBlank(string name, IReadOnlyList<EditableTile>? initialTiles = null)
    {
        var slug = TileSetResourcePaths.Slugify(name);
        return new EditableTileSet(name, TileSetResourcePaths.TileSetPath(slug), initialTiles ?? Array.Empty<EditableTile>(), isAttached: false);
    }
}
