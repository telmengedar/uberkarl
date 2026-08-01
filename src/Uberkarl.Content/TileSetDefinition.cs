namespace Uberkarl.Content;

public sealed class TileSetDefinition
{
    public IReadOnlyList<TileDefinition> Tiles { get; init; } = Array.Empty<TileDefinition>();
}
