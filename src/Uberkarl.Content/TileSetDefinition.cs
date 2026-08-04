namespace Uberkarl.Content;

public sealed class TileSetDefinition
{
    public IReadOnlyList<TileDefinition> Tiles { get; init; } = Array.Empty<TileDefinition>();

    /// <summary>
    /// The tile set's logical terrain groups (DiVoid #7551 Phase 3, design #7580 §7). Empty for a tile set
    /// that declares no terrains (every tile simple or animated only) — omitted from JSON when empty so
    /// pre-Phase-3 content loads unchanged.
    /// </summary>
    public IReadOnlyList<TerrainSetDefinition> TerrainSets { get; init; } = Array.Empty<TerrainSetDefinition>();
}
