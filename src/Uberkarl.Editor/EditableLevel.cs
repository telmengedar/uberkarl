using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// The editor's single source of truth for a level being authored: dimensions, the tile palette, the
/// layer grids, and spawns. It is engine-agnostic (no Godot types) so the apply-edit and save/load
/// round-trip logic can be unit-tested outside the engine.
///
/// <b>Package-as-VFS correction (DiVoid #7571/#7572):</b> a level is pure <b>content</b> — one resource
/// living inside a package (an archive of many typed resources), not the package itself. Archive
/// identity (<c>PackageId</c>/Name/Version/Attribution/ForkedFrom) has moved OUT of this class and lives
/// on <see cref="PackageContext"/> instead; <see cref="Rename"/> here changes only this level's own
/// display name, never a package's. <see cref="LevelPath"/> is this level's own in-package address —
/// namespaced per level (<see cref="LevelResourcePaths"/>) so two distinctly-named levels in the same
/// package never collide — and stays fixed once <see cref="IsAttached"/> (renaming the level does not
/// move its VFS entry); see <see cref="Attach"/>.
///
/// <b>Shared-tileset correction (DiVoid #7551 Phase 1a, design #7580):</b> a level no longer OWNS its
/// tileset. It binds one by <see cref="TileSetReference"/> — a <see cref="ResourceReference"/> to a
/// standalone <see cref="EditableTileSet"/> resource that many levels may reference — via
/// <see cref="BindTileSet"/>. <see cref="Tiles"/> remains the level's read-only palette CACHE for painting
/// (kept in sync with whatever tile set is currently bound), but it is no longer this level's own
/// resource contribution on save (see <see cref="LevelMergeWriter"/>): <see cref="Attach"/> therefore no
/// longer remaps tile graphic paths — those belong to the bound tile set's own namespace
/// (<see cref="TileSetResourcePaths"/>), untouched by a level's attach/rename.
///
/// Edit commands mutate a layer's cells in place; structural changes (resize, add/remove layers or
/// tiles, edit spawns) are deferred increments — for this increment the geometry and palette are fixed
/// at load/create time and only cell contents (and the layer stack, via the structural mutations below)
/// change.
/// </summary>
public sealed class EditableLevel
{
    // The layer stack is mutable in the authoring model (create/delete/reorder/property-set); Layers
    // exposes the same list as IReadOnlyList so every existing reader keeps working unchanged.
    private readonly List<EditableLayer> layers;
    private IReadOnlyList<EditableTile> tiles;
    private IReadOnlyList<EditableTerrainSet> terrainSets;

    public EditableLevel(
        string name,
        ResourcePath levelPath,
        ResourceReference tileSetReference,
        int tileSize,
        int width,
        int height,
        string? backgroundColor,
        IReadOnlyDictionary<string, GridPosition> spawns,
        string? defaultSpawn,
        IReadOnlyList<EditableTile> tiles,
        IReadOnlyList<EditableLayer> layers,
        bool isAttached = false,
        IReadOnlyList<EditableTerrainSet>? terrainSets = null,
        IReadOnlyDictionary<(int Layer, GridPosition Cell), ResolvedBehaviorBinding?>? tileBehaviorOverrides = null,
        IReadOnlyList<ResolvedAreaTrigger>? triggers = null,
        IReadOnlyList<ResolvedObjectPlacement>? objects = null,
        ResolvedBehaviorBinding? levelScript = null)
    {
        if (tileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        Name = name ?? throw new ArgumentNullException(nameof(name));
        LevelPath = levelPath;
        TileSetReference = tileSetReference;
        TileSize = tileSize;
        Width = width;
        Height = height;
        BackgroundColor = backgroundColor;
        Spawns = spawns ?? throw new ArgumentNullException(nameof(spawns));
        DefaultSpawn = defaultSpawn;
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.terrainSets = terrainSets ?? Array.Empty<EditableTerrainSet>();
        if (layers is null)
            throw new ArgumentNullException(nameof(layers));
        this.layers = new List<EditableLayer>(layers);
        IsAttached = isAttached;
        TileBehaviorOverrides = tileBehaviorOverrides ?? new Dictionary<(int Layer, GridPosition Cell), ResolvedBehaviorBinding?>();
        Triggers = triggers ?? Array.Empty<ResolvedAreaTrigger>();
        Objects = objects ?? Array.Empty<ResolvedObjectPlacement>();
        LevelScript = levelScript;

        var expected = width * height;
        foreach (var layer in this.layers)
        {
            if (layer.Cells.Length != expected)
                throw new ArgumentException(
                    $"Layer '{layer.Name}' has {layer.Cells.Length} cells but the {width}x{height} grid needs {expected}.");
        }
    }

    public string Name { get; private set; }

    /// <summary>This level's own in-package path (levels/&lt;slug&gt;.json once attached). Fixed once
    /// <see cref="IsAttached"/> — see <see cref="Attach"/>.</summary>
    public ResourcePath LevelPath { get; private set; }

    /// <summary>
    /// The shared tile set resource this level currently binds (DiVoid #7551 Phase 1a) — a reference, not
    /// an ownership relationship. Set at creation and changed only via <see cref="BindTileSet"/> (the
    /// level-side "bind tileset" affordance, Phase 1b).
    /// </summary>
    public ResourceReference TileSetReference { get; private set; }

    public int TileSize { get; }

    /// <summary>The grid's width in cells. Mutated only by <see cref="Resize"/> (DiVoid #7550).</summary>
    public int Width { get; private set; }

    /// <summary>The grid's height in cells. Mutated only by <see cref="Resize"/> (DiVoid #7550).</summary>
    public int Height { get; private set; }

    public string? BackgroundColor { get; }

    public IReadOnlyDictionary<string, GridPosition> Spawns { get; }

    public string? DefaultSpawn { get; }

    public IReadOnlyList<EditableTile> Tiles => tiles;

    /// <summary>
    /// The bound tile set's terrain sets, cached here exactly like <see cref="Tiles"/> — kept in sync via
    /// <see cref="BindTileSet"/>/<see cref="RefreshTiles"/> (DiVoid #7551 Phase 3). Used by
    /// <see cref="IsPlaceableTerrain"/> to validate a terrain paint and by <see cref="EditableLevelSnapshot"/>
    /// to build the live canvas preview's terrain data — the same "editor preview must resolve terrains
    /// exactly like the runtime" requirement animation already established (design #7580 §9).
    /// </summary>
    public IReadOnlyList<EditableTerrainSet> TerrainSets => terrainSets;

    /// <summary>
    /// Whether this level already occupies a stable, namespaced resource slot in some package — true for
    /// a level just loaded via <see cref="EditableLevelReader"/>, or one that has completed at least one
    /// merge-save via <see cref="Attach"/>. False for a freshly <see cref="CreateBlank"/> level: it has
    /// provisional paths that are never persisted, and must go through Save-As (which calls
    /// <see cref="Attach"/>) before its resources are namespaced for real.
    /// </summary>
    public bool IsAttached { get; private set; }

    /// <summary>The layer stack, back→front (array order is draw order). Mutated via
    /// <see cref="AppendLayer"/>/<see cref="RemoveLayerAt"/>/<see cref="MoveLayer"/>/<see cref="SetLayerProperties"/>.</summary>
    public IReadOnlyList<EditableLayer> Layers => layers;

    /// <summary>
    /// The level's sparse per-instance tile-behavior override/removal map (DiVoid #7738, design #7704 §6),
    /// carried through unedited from whatever the package authored (DiVoid #7747 — read-through only; there
    /// is no authoring UX for this yet, P3 scope per #7738's own known-gap note). Keyed exactly like
    /// <see cref="Content.ResolvedLevel.TileBehaviorOverrides"/> so <see cref="EditableLevelSnapshot"/> can
    /// pass it straight through to the <c>ResolvedLevel</c> a playtest run actually plays.
    /// </summary>
    public IReadOnlyDictionary<(int Layer, GridPosition Cell), ResolvedBehaviorBinding?> TileBehaviorOverrides { get; }

    /// <summary>
    /// The level's grid-rect area triggers (DiVoid #7738, design #7704 §6), carried through unedited —
    /// same read-through-only status as <see cref="TileBehaviorOverrides"/> (DiVoid #7747).
    /// </summary>
    public IReadOnlyList<ResolvedAreaTrigger> Triggers { get; }

    /// <summary>
    /// The level's placed free-moving objects (DiVoid #7863, design #7704 §5.2/§6), carried through unedited
    /// — same read-through-only status as <see cref="Triggers"/> (no authoring UX yet, P3 scope).
    /// </summary>
    public IReadOnlyList<ResolvedObjectPlacement> Objects { get; }

    /// <summary>
    /// The level's global lifecycle/<c>onUpdate</c> script binding (DiVoid #7738, design #7704 §6), or
    /// <c>null</c> when the level declares none — carried through unedited, same status as
    /// <see cref="TileBehaviorOverrides"/> (DiVoid #7747).
    /// </summary>
    public ResolvedBehaviorBinding? LevelScript { get; }

    /// <summary>Whether <paramref name="x"/>,<paramref name="y"/> is a cell inside the grid.</summary>
    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Row-major index of a cell, or -1 when out of bounds.</summary>
    public int CellIndex(int x, int y) => InBounds(x, y) ? y * Width + x : -1;

    /// <summary>The current tile id at a cell, or <see cref="LayerDefinition.EmptyCell"/> when out of bounds.</summary>
    public int GetCell(int layerIndex, int x, int y)
    {
        var index = CellIndex(x, y);
        if (index < 0 || layerIndex < 0 || layerIndex >= Layers.Count)
            return LayerDefinition.EmptyCell;
        return Layers[layerIndex].Cells[index];
    }

    /// <summary>True when <paramref name="tileId"/> is the empty marker or a declared palette tile.</summary>
    public bool IsPlaceableTile(int tileId)
    {
        if (tileId == LayerDefinition.EmptyCell)
            return true;
        foreach (var tile in Tiles)
        {
            if (tile.Id == tileId)
                return true;
        }

        return false;
    }

    /// <summary>True when <paramref name="terrainId"/> is the empty marker or a declared terrain of the currently-bound tile set (DiVoid #7551 Phase 3).</summary>
    public bool IsPlaceableTerrain(int terrainId)
    {
        if (terrainId == LayerDefinition.EmptyCell)
            return true;
        foreach (var terrainSet in terrainSets)
        {
            foreach (var terrain in terrainSet.Terrains)
            {
                if (terrain.Id == terrainId)
                    return true;
            }
        }

        return false;
    }

    // ----- layer structural mutations (create / delete / reorder / property edit) -----

    /// <summary>
    /// Appends a new layer sized to the level's full grid (every cell empty), with its properties
    /// coerced through <see cref="LayerPropertyRules"/>. Draw order is array order, so the new layer
    /// becomes the front-most. Returns the new layer's index (always <c>Layers.Count - 1</c>).
    /// </summary>
    public int AppendLayer(string name, bool collision, float scrollSpeed, bool repeat)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Layer name must not be empty.", nameof(name));

        var cells = new int[Width * Height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var coerced = LayerPropertyRules.Coerce(collision, scrollSpeed, repeat);
        layers.Add(new EditableLayer(name, coerced.Collision, coerced.ScrollSpeed, coerced.Repeat, cells));
        return layers.Count - 1;
    }

    /// <summary>
    /// Removes the layer at <paramref name="index"/>. Refuses (returns <c>false</c>, no-op) when
    /// <paramref name="index"/> is out of range or it is the level's <b>last</b> layer — a level must
    /// always have at least one layer to paint on. Shifts every later layer's index down by one, so a
    /// caller holding recorded cell-undo commands (which store an absolute layer index) must clear that
    /// history after a successful removal.
    /// </summary>
    public bool RemoveLayerAt(int index)
    {
        if (index < 0 || index >= layers.Count)
            return false;
        if (layers.Count <= 1)
            return false;

        layers.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Swaps the layer at <paramref name="index"/> with its adjacent neighbour in the direction of
    /// <paramref name="direction"/> (positive = toward the front/end, negative = toward the back/start) —
    /// this <b>is</b> the draw-order change. Clamped at the ends: swapping past an end is a no-op, and
    /// the returned index is unchanged from <paramref name="index"/> in that case. A successful swap
    /// shifts the moved (and displaced) layer's index, so a caller holding recorded cell-undo commands
    /// must clear that history after a real (non-no-op) move.
    /// </summary>
    public int MoveLayer(int index, int direction)
    {
        if (index < 0 || index >= layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var target = index + Math.Sign(direction);
        if (target < 0 || target >= layers.Count)
            return index;

        (layers[index], layers[target]) = (layers[target], layers[index]);
        return target;
    }

    /// <summary>
    /// Renames the layer at <paramref name="index"/> (DiVoid #7513 — layer rename, the first consumer of
    /// the on-screen keyboard). Replaces the <see cref="EditableLayer"/> instance but reuses its
    /// <c>Cells</c> array and every other property unchanged, exactly like <see cref="SetLayerProperties"/>
    /// — index-stable, so recorded cell-edit history survives a rename. Returns <c>false</c> (no-op) when
    /// <paramref name="name"/> equals the layer's current name (ordinal comparison).
    /// </summary>
    public bool RenameLayer(int index, string name)
    {
        if (index < 0 || index >= layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Layer name must not be empty.", nameof(name));

        var current = layers[index];
        if (string.Equals(current.Name, name, StringComparison.Ordinal))
            return false;

        layers[index] = new EditableLayer(name, current.Collision, current.ScrollSpeed, current.Repeat, current.Cells, current.Terrain);
        return true;
    }

    /// <summary>
    /// Sets the layer at <paramref name="index"/>'s properties, coerced through
    /// <see cref="LayerPropertyRules"/>. Replaces the <see cref="EditableLayer"/> instance but reuses its
    /// <c>Cells</c> array unchanged, so recorded cell-edit commands (which re-resolve
    /// <c>Layers[i].Cells</c> on apply/revert) keep working across the property edit — this is why a
    /// property edit, unlike delete/move, does not need to clear cell-edit history. Returns <c>false</c>
    /// (no-op) when the coerced triple equals the layer's current one.
    /// </summary>
    public bool SetLayerProperties(int index, bool collision, float scrollSpeed, bool repeat)
    {
        if (index < 0 || index >= layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var current = layers[index];
        var coerced = LayerPropertyRules.Coerce(collision, scrollSpeed, repeat);
        if (current.Collision == coerced.Collision && current.ScrollSpeed == coerced.ScrollSpeed && current.Repeat == coerced.Repeat)
            return false;

        layers[index] = new EditableLayer(current.Name, coerced.Collision, coerced.ScrollSpeed, coerced.Repeat, current.Cells, current.Terrain);
        return true;
    }

    // ----- grid resize (DiVoid #7550) -----

    /// <summary>
    /// True when resizing to <paramref name="width"/>x<paramref name="height"/> would crop away at least
    /// one painted (non-empty) cell on any layer. Pure query — never mutates the level. This is the seam
    /// the UI reads to decide whether <see cref="Resize"/> needs a confirm press first (the same shape as
    /// the layer-manager's delete confirm, since a resize that crops painted tiles is not undoable — see
    /// <see cref="LevelEditSession.Resize"/>). Always <c>false</c> when neither dimension shrinks — growing
    /// never crops anything.
    /// </summary>
    public bool WouldDropPaintedCells(int width, int height)
    {
        if (width >= Width && height >= Height)
            return false;

        foreach (var layer in layers)
        {
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (x >= width || y >= height)
                    {
                        var index = y * Width + x;
                        // DiVoid #7551 Phase 3: a terrain-painted cell has Cells==EmptyCell by the two-channel
                        // invariant, so it must be checked separately or a shrink could silently crop painted
                        // terrain without the confirm prompt this query exists to trigger.
                        if (layer.Cells[index] != LayerDefinition.EmptyCell || layer.Terrain[index] != LayerDefinition.EmptyCell)
                            return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resizes the grid to <paramref name="width"/>x<paramref name="height"/>, applied identically across
    /// every layer — levels share one W×H across their whole layer stack (DiVoid #7420). Growing preserves
    /// every existing cell at its original (x,y) coordinate and fills the newly-added cells empty;
    /// shrinking crops away whatever falls outside the new bounds. <see cref="TileSize"/> is untouched —
    /// this is purely a grid-dimension change, never a re-scale. Each layer's <see cref="EditableLayer"/>
    /// instance is replaced (a resize necessarily reallocates the <c>Cells</c> array — unlike
    /// <see cref="SetLayerProperties"/> there is no existing array of the right length to reuse). Returns
    /// <c>false</c> (no-op) when <paramref name="width"/>/<paramref name="height"/> already equal the
    /// level's current size. This method does not itself ask for confirmation — see
    /// <see cref="WouldDropPaintedCells"/> for the query the caller should make first.
    /// </summary>
    public bool Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (width == Width && height == Height)
            return false;

        var copyWidth = Math.Min(Width, width);
        var copyHeight = Math.Min(Height, height);

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var cells = new int[width * height];
            var terrain = new int[width * height];
            Array.Fill(cells, LayerDefinition.EmptyCell);
            Array.Fill(terrain, LayerDefinition.EmptyCell);
            for (var y = 0; y < copyHeight; y++)
            {
                for (var x = 0; x < copyWidth; x++)
                {
                    cells[y * width + x] = layer.Cells[y * Width + x];
                    terrain[y * width + x] = layer.Terrain[y * Width + x];
                }
            }

            layers[i] = new EditableLayer(layer.Name, layer.Collision, layer.ScrollSpeed, layer.Repeat, cells, terrain);
        }

        Width = width;
        Height = height;
        return true;
    }

    /// <summary>
    /// Renames the level's own display name (DiVoid #7552 — Save-As level naming via the on-screen
    /// keyboard). Under the package-as-VFS correction this is <b>content-only</b>: it never touches a
    /// package's identity, and once <see cref="IsAttached"/> it does not move <see cref="LevelPath"/>/
    /// <see cref="TileSetPath"/> either (design #7572 open question 3 — renaming content must not move a
    /// VFS entry or break references into it; see <see cref="Attach"/> for the one path that does
    /// (re)namespace a level's resources). Returns <c>false</c> (no-op) when <paramref name="name"/>
    /// equals the current name (ordinal comparison) — mirrors <see cref="RenameLayer"/>.
    /// </summary>
    public bool Rename(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Level name must not be empty.", nameof(name));
        if (string.Equals(Name, name, StringComparison.Ordinal))
            return false;

        Name = name;
        return true;
    }

    /// <summary>
    /// Establishes (or re-establishes) this level's namespaced resource slot in a package: derives
    /// <see cref="LevelPath"/> from <paramref name="slug"/> (<see cref="LevelResourcePaths"/>), or reuses
    /// <paramref name="overwriteLevelPath"/> verbatim when the author explicitly picked an existing level
    /// resource to overwrite. Under the shared-tileset correction (DiVoid #7551 Phase 1a) this no longer
    /// touches tile graphic paths at all — those belong to whichever tile set is currently bound
    /// (<see cref="TileSetReference"/>), which has its own independent attach lifecycle
    /// (<see cref="EditableTileSet.Attach"/>) untouched by a level's own rename/re-namespace. Marks
    /// <see cref="IsAttached"/> so a later plain Save reuses this exact path without re-deriving it from
    /// whatever the level is renamed to next (design #7572 open question 3). Called by
    /// <see cref="LevelEditSession.AttachAsNewResource"/>/<see cref="LevelEditSession.AttachToExistingResource"/>
    /// — the two Save-As outcomes that establish or confirm a level's slot; a plain re-save into an
    /// already-attached level never calls this.
    /// </summary>
    public void Attach(string slug, ResourcePath? overwriteLevelPath)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug must not be empty.", nameof(slug));

        LevelPath = overwriteLevelPath ?? LevelResourcePaths.LevelPath(slug);
        IsAttached = true;
    }

    /// <summary>
    /// Rebinds this level to a different shared tile set resource (DiVoid #7551 Phase 1b — the level-side
    /// "bind tileset" affordance): sets <see cref="TileSetReference"/> and replaces the palette cache
    /// (<see cref="Tiles"/>) with the newly-bound tile set's tiles. Does not validate that every already-
    /// painted cell resolves against the new palette (design #7580 §11 — a full "surfaces a validation
    /// warning, not a silent break" guard is later work); a stray dangling cell id would still be caught,
    /// typed, by the loader on next load/save. Also the seam <see cref="RefreshTiles"/> uses to keep the
    /// cache in sync after an in-place edit to the SAME bound tile set (add/remove/rename a tile via
    /// <c>TileSetEditor</c> without rebinding to a different resource).
    /// </summary>
    public void BindTileSet(ResourceReference tileSetReference, IReadOnlyList<EditableTile> tiles, IReadOnlyList<EditableTerrainSet>? terrainSets = null)
    {
        TileSetReference = tileSetReference;
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.terrainSets = terrainSets ?? Array.Empty<EditableTerrainSet>();
    }

    /// <summary>
    /// Re-syncs the palette cache from the currently-bound tile set's live tile list (and, DiVoid #7551
    /// Phase 3, its terrain sets), without changing which tile set is bound — the seam a mutation made
    /// through <c>TileSetEditor</c> (add/remove/rename a tile, define/edit a terrain, in the tile set this
    /// level already has open) uses to keep <see cref="Tiles"/>/<see cref="TerrainSets"/> current.
    /// </summary>
    public void RefreshTiles(IReadOnlyList<EditableTile> tiles, IReadOnlyList<EditableTerrainSet>? terrainSets = null)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.terrainSets = terrainSets ?? Array.Empty<EditableTerrainSet>();
    }

    /// <summary>
    /// Creates an empty level: one collision layer of the given size filled with empty cells, bound to
    /// <paramref name="tileSetReference"/> with <paramref name="palette"/> as the initial cache of that
    /// tile set's tiles (the caller resolves the actual tiles — the model never encodes images or reads
    /// packages). Used by the editor's "New" action, which mints a fresh <see cref="EditableTileSet"/>
    /// (seeded from <c>DefaultPalette</c>) alongside the level so "New" still opens paintable, then binds
    /// it here. The level starts <b>unattached</b> (<see cref="IsAttached"/> is <c>false</c>) with a
    /// provisional <see cref="LevelPath"/> derived from <paramref name="name"/> — never persisted as-is;
    /// the first Save routes through Save-As, which calls <see cref="Attach"/> to establish the level's
    /// real namespaced slot from whatever name is actually typed there.
    /// </summary>
    public static EditableLevel CreateBlank(
        string name,
        int tileSize,
        int width,
        int height,
        ResourceReference tileSetReference,
        IReadOnlyList<EditableTile> palette)
    {
        if (palette is null)
            throw new ArgumentNullException(nameof(palette));

        var cells = new int[width * height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);

        var slug = LevelResourcePaths.Slugify(name);
        return new EditableLevel(
            name,
            LevelResourcePaths.LevelPath(slug),
            tileSetReference,
            tileSize,
            width,
            height,
            backgroundColor: null,
            new Dictionary<string, GridPosition>(),
            defaultSpawn: null,
            palette,
            new[] { layer },
            isAttached: false);
    }
}
