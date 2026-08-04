namespace Uberkarl.Content;

/// <summary>
/// A group of related logical terrains sharing one matching mode (DiVoid #7551 Phase 3, design #7580 §7) —
/// mirrors Godot 4's terrain SET concept (as opposed to an individual terrain). Belongs to exactly one
/// <see cref="TileSetDefinition"/>.
/// </summary>
public sealed class TerrainSetDefinition
{
    /// <summary>Stable id, unique within the tile set's <see cref="TileSetDefinition.TerrainSets"/>. Never reused (design #7580 §11).</summary>
    public int Id { get; init; }

    /// <summary>Author-facing name (e.g. "Ground Types").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Which of a tile's eight neighbours participate in this set's auto-tile matching. A single, set-wide
    /// choice (design #7580 §7 — "matching mode is a terrain-set-level choice set once").
    /// </summary>
    public TerrainMatchMode MatchingMode { get; init; } = TerrainMatchMode.CornersAndSides;

    /// <summary>The logical terrains belonging to this set.</summary>
    public IReadOnlyList<TerrainDefinition> Terrains { get; init; } = Array.Empty<TerrainDefinition>();
}
