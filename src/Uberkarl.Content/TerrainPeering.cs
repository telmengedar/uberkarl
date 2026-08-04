namespace Uberkarl.Content;

/// <summary>
/// The eight directions around a tile (DiVoid #7551 Phase 3, design #7580 §14 — "a 3×3 cell diagram,
/// center = the tile, 8 surrounds"). A <see cref="TileDefinition"/> that is a terrain variant sets the
/// bits for the neighbours that must belong to the SAME terrain for this specific variant to be the one
/// Godot's terrain-connect resolution picks — e.g. an "all sides + all corners" bitmask is the terrain's
/// solid interior tile; a single missing side is that side's edge tile. Engine-agnostic: <c>TileSetBuilder</c>
/// is the only place this is translated into Godot's <c>TileSet.CellNeighbor</c> peering-bit calls.
/// </summary>
[Flags]
public enum TerrainPeering
{
    None = 0,
    North = 1 << 0,
    NorthEast = 1 << 1,
    East = 1 << 2,
    SouthEast = 1 << 3,
    South = 1 << 4,
    SouthWest = 1 << 5,
    West = 1 << 6,
    NorthWest = 1 << 7,

    /// <summary>Every side and corner — the terrain's fully-interior variant.</summary>
    All = North | NorthEast | East | SouthEast | South | SouthWest | West | NorthWest,
}
