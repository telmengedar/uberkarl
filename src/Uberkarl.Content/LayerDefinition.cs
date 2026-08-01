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
}
