namespace Uberkarl.Content;

/// <summary>
/// A cell coordinate in the level grid (tile units, not pixels). Used, for example,
/// to express where a player spawns.
/// </summary>
public readonly record struct GridPosition(int X, int Y);
