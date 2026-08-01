namespace Uberkarl.Content;

public sealed class ResolvedLevel
{
    public int TileSize { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public IReadOnlyList<ResolvedLayer> Layers { get; init; } = Array.Empty<ResolvedLayer>();

    public IReadOnlyDictionary<int, byte[]> TileGraphics { get; init; } = new Dictionary<int, byte[]>();

    /// <summary>Tile ids flagged as solid in the tile set. Only enforced on <see cref="LayerRole.Main"/> layers.</summary>
    public IReadOnlySet<int> CollidingTileIds { get; init; } = new HashSet<int>();

    /// <summary>Optional player spawn cell (tile units); null when the level does not specify one.</summary>
    public GridPosition? PlayerStart { get; init; }
}

public sealed class ResolvedLayer
{
    public string Name { get; init; } = string.Empty;

    public LayerRole Role { get; init; } = LayerRole.Background;

    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();
}
