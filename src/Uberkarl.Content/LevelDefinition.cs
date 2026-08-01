using Uberkarl.Packages;

namespace Uberkarl.Content;

public sealed class LevelDefinition
{
    public int TileSize { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public ResourceReference TileSet { get; init; }

    public IReadOnlyList<LayerDefinition> Layers { get; init; } = Array.Empty<LayerDefinition>();
}
