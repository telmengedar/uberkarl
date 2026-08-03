using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// The editor's single source of truth for a level being authored: dimensions, the tile palette, the
/// layer grids, spawns, and the package metadata/paths needed to re-save it. It is engine-agnostic
/// (no Godot types) so the apply-edit and save/load round-trip logic can be unit-tested outside the
/// engine. Edit commands mutate a layer's cells in place; structural changes (resize, add/remove
/// layers or tiles, edit spawns) are deferred increments — for this increment the geometry, palette,
/// and spawns are fixed at load/create time and only cell contents change.
/// </summary>
public sealed class EditableLevel
{
    /// <summary>Default in-package path a newly created level's definition is written to.</summary>
    public static readonly ResourcePath DefaultLevelPath = ResourcePath.Create("levels/level.json");

    /// <summary>Default in-package path a newly created level's tile set is written to.</summary>
    public static readonly ResourcePath DefaultTileSetPath = ResourcePath.Create("tileset.json");

    // The layer stack is mutable in the authoring model (create/delete/reorder/property-set); Layers
    // exposes the same list as IReadOnlyList so every existing reader keeps working unchanged.
    private readonly List<EditableLayer> layers;

    public EditableLevel(
        PackageId packageId,
        string name,
        string version,
        Attribution? attribution,
        PackageId? forkedFrom,
        ResourcePath levelPath,
        ResourcePath tileSetPath,
        int tileSize,
        int width,
        int height,
        string? backgroundColor,
        IReadOnlyDictionary<string, GridPosition> spawns,
        string? defaultSpawn,
        IReadOnlyList<EditableTile> tiles,
        IReadOnlyList<EditableLayer> layers)
    {
        if (tileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        PackageId = packageId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Attribution = attribution;
        ForkedFrom = forkedFrom;
        LevelPath = levelPath;
        TileSetPath = tileSetPath;
        TileSize = tileSize;
        Width = width;
        Height = height;
        BackgroundColor = backgroundColor;
        Spawns = spawns ?? throw new ArgumentNullException(nameof(spawns));
        DefaultSpawn = defaultSpawn;
        Tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        if (layers is null)
            throw new ArgumentNullException(nameof(layers));
        this.layers = new List<EditableLayer>(layers);

        var expected = width * height;
        foreach (var layer in this.layers)
        {
            if (layer.Cells.Length != expected)
                throw new ArgumentException(
                    $"Layer '{layer.Name}' has {layer.Cells.Length} cells but the {width}x{height} grid needs {expected}.");
        }
    }

    public PackageId PackageId { get; }

    public string Name { get; }

    public string Version { get; }

    public Attribution? Attribution { get; }

    public PackageId? ForkedFrom { get; }

    public ResourcePath LevelPath { get; }

    public ResourcePath TileSetPath { get; }

    public int TileSize { get; }

    public int Width { get; }

    public int Height { get; }

    public string? BackgroundColor { get; }

    public IReadOnlyDictionary<string, GridPosition> Spawns { get; }

    public string? DefaultSpawn { get; }

    public IReadOnlyList<EditableTile> Tiles { get; }

    /// <summary>The layer stack, back→front (array order is draw order). Mutated via
    /// <see cref="AppendLayer"/>/<see cref="RemoveLayerAt"/>/<see cref="MoveLayer"/>/<see cref="SetLayerProperties"/>.</summary>
    public IReadOnlyList<EditableLayer> Layers => layers;

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

        layers[index] = new EditableLayer(name, current.Collision, current.ScrollSpeed, current.Repeat, current.Cells);
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

        layers[index] = new EditableLayer(current.Name, coerced.Collision, coerced.ScrollSpeed, coerced.Repeat, current.Cells);
        return true;
    }

    /// <summary>
    /// Creates an empty level: a fresh package id, one collision layer of the given size filled with
    /// empty cells, and the supplied palette (the caller provides the tile graphics — the model never
    /// encodes images). Used by the editor's "New" action; the palette is fixed because tile-set
    /// editing is a later increment.
    /// </summary>
    public static EditableLevel CreateBlank(
        string name,
        int tileSize,
        int width,
        int height,
        IReadOnlyList<EditableTile> palette)
    {
        if (palette is null)
            throw new ArgumentNullException(nameof(palette));

        var cells = new int[width * height];
        Array.Fill(cells, LayerDefinition.EmptyCell);
        var layer = new EditableLayer("terrain", collision: true, scrollSpeed: 1f, repeat: false, cells);

        return new EditableLevel(
            PackageId.New(),
            name,
            "0.1.0",
            new Attribution { Author = "Uberkarl", License = "CC0-1.0" },
            forkedFrom: null,
            DefaultLevelPath,
            DefaultTileSetPath,
            tileSize,
            width,
            height,
            backgroundColor: null,
            new Dictionary<string, GridPosition>(),
            defaultSpawn: null,
            palette,
            new[] { layer });
    }
}
