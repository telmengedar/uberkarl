using Uberkarl.Packages;

namespace Uberkarl.Content;

public sealed class LevelDefinition
{
    public int TileSize { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public ResourceReference TileSet { get; init; }

    /// <summary>
    /// Optional grid cell where the player spawns. When absent, the game falls back to a
    /// sensible default spawn convention.
    /// </summary>
    public GridPosition? PlayerStart { get; init; }

    public IReadOnlyList<LayerDefinition> Layers { get; init; } = Array.Empty<LayerDefinition>();
}
