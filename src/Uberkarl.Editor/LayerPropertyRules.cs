namespace Uberkarl.Editor;

/// <summary>
/// The single authoring-side encoding of the layer schema's invariant:
/// <c>collision ⇒ scrollSpeed == 1.0 ∧ ¬repeat</c> (see <c>Content.LevelLoader.ValidateLayer</c>, which
/// remains the enforcement backstop at load time). Collision is dominant — turning it on snaps scroll
/// and repeat to their world-locked values. This is the seam the UI reads both to coerce a proposed
/// property set and to decide whether the scroll/repeat controls should be editable at all, so the
/// invalid combination is structurally unreachable rather than merely rejected.
/// </summary>
public static class LayerPropertyRules
{
    /// <summary>
    /// A coerced, always-valid property triple, plus which of the dependent fields (if any) were forced
    /// away from the caller's proposed value because collision is on.
    /// </summary>
    public readonly record struct Coerced(bool Collision, float ScrollSpeed, bool Repeat, bool ScrollSpeedForced, bool RepeatForced);

    /// <summary>World-locked scroll speed every collision layer is pinned to.</summary>
    public const float CollisionScrollSpeed = 1.0f;

    /// <summary>
    /// Coerces a proposed <paramref name="collision"/>/<paramref name="scrollSpeed"/>/<paramref name="repeat"/>
    /// triple into a valid one. When <paramref name="collision"/> is <c>false</c> the triple passes through
    /// unchanged. When it is <c>true</c>, <paramref name="scrollSpeed"/> is forced to
    /// <see cref="CollisionScrollSpeed"/> and <paramref name="repeat"/> is forced to <c>false</c>.
    /// </summary>
    public static Coerced Coerce(bool collision, float scrollSpeed, bool repeat)
    {
        if (!collision)
            return new Coerced(false, scrollSpeed, repeat, ScrollSpeedForced: false, RepeatForced: false);

        bool scrollForced = scrollSpeed != CollisionScrollSpeed;
        bool repeatForced = repeat;
        return new Coerced(true, CollisionScrollSpeed, false, scrollForced, repeatForced);
    }

    /// <summary>
    /// Whether the scroll-speed and repeat controls should be editable for a layer whose collision flag
    /// currently has this value. <c>false</c> exactly when <paramref name="collision"/> is <c>true</c> —
    /// the seam the <c>LayerManagerPanel</c> reads to disable/grey those controls.
    /// </summary>
    public static bool Editable(bool collision) => !collision;
}
