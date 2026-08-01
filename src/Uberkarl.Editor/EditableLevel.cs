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
        Layers = layers ?? throw new ArgumentNullException(nameof(layers));

        var expected = width * height;
        foreach (var layer in Layers)
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

    public IReadOnlyList<EditableLayer> Layers { get; }

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
