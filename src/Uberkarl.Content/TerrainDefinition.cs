namespace Uberkarl.Content;

/// <summary>
/// One logical terrain "type" (DiVoid #7551 Phase 3, design #7580 §7) — e.g. "earth". Belongs to exactly
/// one <see cref="TerrainSetDefinition"/>. This is the thing an author PAINTS on a level layer's terrain
/// channel (<see cref="LayerDefinition.Terrain"/>); which real, concrete <see cref="TileDefinition"/>
/// variant renders at a given cell is resolved from the surrounding pattern at build time, never stored.
/// </summary>
public sealed class TerrainDefinition
{
    /// <summary>
    /// Stable id, unique across the WHOLE tile set (not just within its terrain set) — this is what a
    /// layer's terrain channel and a tile's <see cref="TileDefinition.Terrain"/> membership reference, the
    /// same "never reused, detects a dangling reference" stability <see cref="TileDefinition.Id"/> has
    /// (design #7580 §11).
    /// </summary>
    public int Id { get; init; }

    /// <summary>Author-facing name (e.g. "Earth").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Author-facing colour (a hex string, same <c>#RRGGBB</c>/<c>#RRGGBBAA</c> convention as
    /// <see cref="LevelDefinition.BackgroundColor"/>) — used as the terrain's swatch in the authoring UI and
    /// as Godot's own terrain colour (shown in its native terrain-paint tooling, unused at runtime).
    /// </summary>
    public string? Color { get; init; }
}
