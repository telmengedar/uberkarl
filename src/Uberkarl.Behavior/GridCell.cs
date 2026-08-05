namespace Uberkarl.Behavior;

/// <summary>
/// A cell coordinate in the level grid (tile units, not pixels) — mirrors <c>Uberkarl.Content.GridPosition</c>'s
/// shape. Deliberately not a dependency on <c>Uberkarl.Content</c>: the content pipeline is expected to grow a
/// dependency on this core (design #7704 §16 build step 2 — <c>LevelDefinition</c>/<c>TileDefinition</c> gain
/// behavior bindings), so the behavior core must not depend back on it.
/// </summary>
public readonly record struct GridCell(int X, int Y);
