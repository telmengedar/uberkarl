namespace Uberkarl.Content;

public sealed class ResolvedLevel
{
    public int TileSize { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public IReadOnlyList<ResolvedLayer> Layers { get; init; } = Array.Empty<ResolvedLayer>();

    public IReadOnlyDictionary<int, byte[]> TileGraphics { get; init; } = new Dictionary<int, byte[]>();
}

public sealed class ResolvedLayer
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();
}
