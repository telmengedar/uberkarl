namespace Uberkarl.Content;

/// <summary>
/// A terrain set's matching mode (DiVoid #7551 Phase 3, design #7580 §7) — which of a tile's eight
/// neighbours (<see cref="TerrainPeering"/>) participate in auto-tile matching. Mirrors Godot 4's
/// <c>TileSet.TerrainMode</c> one-to-one so <c>TileSetBuilder</c> is a direct translation, not a
/// re-interpretation.
/// </summary>
public enum TerrainMatchMode
{
    /// <summary>Only the four corner directions (NE/SE/SW/NW) participate.</summary>
    Corners,

    /// <summary>Only the four side directions (N/E/S/W) participate.</summary>
    Sides,

    /// <summary>All eight directions participate — the common choice for a classic blob/auto-tile terrain.</summary>
    CornersAndSides,
}
