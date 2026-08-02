namespace Uberkarl.Editor.Input;

/// <summary>
/// The one rule that stops directional input from driving two things at once. The grid cursor may only act
/// on a movement request when its surface holds focus <b>and</b> nothing else is currently capturing
/// directional input — an open pop-in radial, or a revealed toolbar/panel focus-zone. While a menu or panel
/// is up, the same stick / D-pad / arrows steer the wheel or navigate the panel, and the grid cursor
/// underneath must stand still. Pure and engine-agnostic so the "cursor frozen while a menu is open"
/// invariant is pinned down without the engine (the Godot canvas just feeds it its focus + captured state).
/// </summary>
public static class CursorInputGate
{
    /// <summary>
    /// True only when the grid cursor should respond to a movement request: the surface owns focus and no
    /// directional-capturing surface (a radial menu or a panel/toolbar focus-zone) is active. When
    /// <paramref name="directionalCaptured"/> is set, this is always false — that is the invariant a menu or
    /// panel relies on to freeze the cursor regardless of which control momentarily holds focus.
    /// </summary>
    public static bool AllowsCursorMovement(bool surfaceHasFocus, bool directionalCaptured)
        => surfaceHasFocus && !directionalCaptured;

    /// <summary>
    /// True only when the canvas's primary action (paint / erase at the grid cursor, the gamepad/keyboard
    /// confirm) should act: the surface owns focus and no radial menu or non-canvas focus-zone is currently
    /// capturing input. The gating condition is the same as cursor movement — while a menu or a toolbar/panel
    /// zone is active, the confirm button belongs to that surface (it activates the focused Control via
    /// <c>ui_accept</c>), so <c>editor_paint</c> must stay inert on the canvas underneath rather than paint a
    /// cell nobody is aiming at. This is the "editor_paint is inert off-canvas" half of the classic-Control
    /// activation fix, pinned down engine-free.
    /// </summary>
    public static bool AllowsPrimaryAction(bool surfaceHasFocus, bool directionalCaptured)
        => surfaceHasFocus && !directionalCaptured;

    /// <summary>
    /// Whether a directional input is currently owned by a menu or a revealed toolbar/panel focus-zone rather
    /// than the grid cursor. True whenever a pop-in radial is open <b>or</b> focus rests on a non-canvas zone
    /// (toolbar / panel). This is the flag the canvas reads (<see cref="AllowsCursorMovement"/>) to freeze its
    /// cursor. It matters because on a real gamepad one physical stick / D-pad press fires <b>both</b> the
    /// editor cursor action <b>and</b> Godot's built-in <c>ui_*</c> focus navigation from the same event: even
    /// if that <c>ui_*</c> navigation momentarily bounces focus back onto the full-rect canvas, a captured
    /// direction keeps the cursor still — the input belongs to the open menu or the navigated panel.
    /// </summary>
    public static bool DirectionCaptured(bool radialOpen, bool nonCanvasZoneActive)
        => radialOpen || nonCanvasZoneActive;
}
