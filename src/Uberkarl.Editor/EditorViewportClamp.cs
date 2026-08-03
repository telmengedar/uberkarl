namespace Uberkarl.Editor;

/// <summary>
/// Clamped scroll-offset arithmetic for the editor's fixed-zoom, cursor-following viewport (Toni's resize
/// playtest fix, DiVoid #7576: "one screen fitting the entire level is not feasible in realistic level
/// sizes"). Given a target world-space centre (the grid cursor's cell) and a panel/level extent along one
/// axis, computes the offset that centres the target, then clamps so the visible world rect never shows
/// past the level bounds — the SAME "LimitLeft/Top=0, LimitRight/Bottom=size" rule
/// <c>PlayRuntimeBuilder.AttachCamera</c> applies to the play camera via Godot's <c>Camera2D</c> Limit*
/// properties, reimplemented here by hand because <c>EditorCanvas</c> has no <c>Camera2D</c> of its own —
/// it renders the level inline inside the shared UI viewport, alongside the toolbar/panels, which a real
/// Camera2D would drag along with it if attached to the same viewport. Pure and engine-agnostic so the
/// centre/clamp math is unit-tested without Godot, mirroring how <see cref="Input.GridCursor"/>'s clamp is
/// tested independent of the engine.
/// </summary>
public static class EditorViewportClamp
{
    /// <summary>
    /// The offset (in panel/screen pixels) that places <paramref name="targetWorldCenter"/> at the centre
    /// of a <paramref name="panelExtent"/>-pixel panel at <paramref name="scale"/> zoom, then clamps so the
    /// panel never shows world space outside <c>[0, levelExtentWorld]</c>. When the level, scaled, is
    /// smaller than the panel, centres it instead — the clamp range would otherwise be inverted, the same
    /// behaviour <c>Camera2D</c> gives when its limits sit inside the viewport.
    /// </summary>
    public static float Offset(float targetWorldCenter, float panelExtent, float levelExtentWorld, float scale)
    {
        float levelExtentScaled = levelExtentWorld * scale;
        if (levelExtentScaled <= panelExtent)
            return (panelExtent - levelExtentScaled) / 2f;

        float desired = panelExtent / 2f - targetWorldCenter * scale;
        float min = panelExtent - levelExtentScaled; // rightmost/bottommost clamp (LimitRight/Bottom)
        const float max = 0f; // leftmost/topmost clamp (LimitLeft/Top = 0)

        if (desired < min)
            return min;
        return desired > max ? max : desired;
    }
}
