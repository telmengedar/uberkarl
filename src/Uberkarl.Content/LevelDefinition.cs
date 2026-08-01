using Uberkarl.Packages;

namespace Uberkarl.Content;

public sealed class LevelDefinition
{
    public int TileSize { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public ResourceReference TileSet { get; init; }

    /// <summary>
    /// Optional solid fill rendered behind every layer, always covering the viewport regardless of
    /// camera position, so a finite parallax layer's edge never hard-cuts to the viewport clear
    /// colour. A hex string (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>); <c>null</c> when the level declares
    /// no fill (the viewport clear colour then shows through, as before). Parsed and validated to an
    /// <see cref="RgbaColor"/> at load time.
    /// </summary>
    public string? BackgroundColor { get; init; }

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
