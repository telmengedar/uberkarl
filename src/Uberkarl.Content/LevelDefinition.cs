using Uberkarl.Packages;

namespace Uberkarl.Content;

public sealed class LevelDefinition
{
    public int TileSize { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public ResourceReference TileSet { get; init; }

    /// <summary>
    /// Named spawn points keyed by spawn name, each a grid cell (tile units). Enables spawning
    /// the player at a chosen entry point; a single default is named by <see cref="DefaultSpawn"/>.
    /// Empty when the level declares no spawns (e.g. a display-only level).
    /// </summary>
    public IReadOnlyDictionary<string, GridPosition> Spawns { get; init; }
        = new Dictionary<string, GridPosition>();

    /// <summary>
    /// The name of the spawn in <see cref="Spawns"/> used when no specific spawn is requested.
    /// Required whenever <see cref="Spawns"/> is non-empty; null/empty only for a level with no spawns.
    /// </summary>
    public string? DefaultSpawn { get; init; }

    public IReadOnlyList<LayerDefinition> Layers { get; init; } = Array.Empty<LayerDefinition>();
}
