namespace Uberkarl.Content;

public sealed class LayerDefinition
{
    public const int EmptyCell = -1;

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Whether this layer is a collision layer. When <c>false</c> the layer never collides,
    /// even for a tile flagged <see cref="TileDefinition.Collides"/>. Draw order is the layer
    /// array order (back to front) and is independent of this flag. Defaults to <c>false</c>
    /// so a layer is display-only unless it opts in.
    /// </summary>
    public bool Collision { get; init; }

    /// <summary>
    /// Per-layer parallax scroll factor relative to the camera. <c>1.0</c> = world-locked (the
    /// layer moves 1:1 with the world); <c>&lt;1.0</c> = a slower-moving background (depth);
    /// <c>&gt;1.0</c> = a faster-moving foreground. Defaults to <c>1.0</c> so a layer is
    /// world-locked unless it opts into parallax; an omitted value in JSON therefore loads as 1.0.
    /// Invariant: a <see cref="Collision"/> layer MUST be world-locked (<c>scrollSpeed == 1.0</c>),
    /// because a parallax layer's on-screen position is not its world position — the loader enforces
    /// this.
    /// </summary>
    public float ScrollSpeed { get; init; } = 1.0f;

    /// <summary>
    /// Whether this layer's content tiles across the scroll extent instead of ending. When <c>true</c>
    /// the layer is rendered through a repeating parallax wrapper whose repeat period is the layer's
    /// content size, so a background repeats seamlessly rather than running out at a finite edge.
    /// Defaults to <c>false</c> (finite). Invariant: a <see cref="Collision"/> layer MUST NOT repeat —
    /// repeating the visuals would not repeat the authored collision geometry, so screen and world
    /// would disagree. The loader enforces this.
    /// </summary>
    public bool Repeat { get; init; }

    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();

    /// <summary>
    /// The logical terrain paint parallel to <see cref="Cells"/> (DiVoid #7551 Phase 3, design #7580 §7/§10):
    /// each entry is a declared <see cref="TerrainDefinition.Id"/>, or <see cref="EmptyCell"/> when that cell
    /// is not terrain-painted. Empty (the default) for a layer that has never had a terrain painted onto it —
    /// omitted from JSON when empty so pre-Phase-3 content loads unchanged. When non-empty it MUST have
    /// exactly as many entries as <see cref="Cells"/> (the loader validates this), and per-cell the two
    /// channels are mutually exclusive: a cell is concrete XOR terrain-painted, never both (design #7580 §7 —
    /// "a cell is either concrete or terrain-marked"). Stores the AUTHOR'S logical paint, never a resolved
    /// concrete id — the whole point of auto-tiling is that the real variant is re-resolved from the
    /// surrounding pattern at build time, so borders re-flow when a neighbour is edited (design #7580 §7,
    /// "chief trade-off").
    /// </summary>
    public IReadOnlyList<int> Terrain { get; init; } = Array.Empty<int>();
}
