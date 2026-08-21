namespace Uberkarl.Editor.Input;

/// <summary>
/// The named, device-neutral editor actions the whole editor reacts to. Nothing in the editor logic
/// looks at raw device input (a mouse button, a key code, a gamepad button) — it reacts to these
/// intents, which the engine layer raises from whatever device produced them. This is the seam that
/// makes the editor input-agnostic: gamepad, keyboard, and mouse all resolve down to the same set of
/// actions with full parity, and a later menu paradigm (pop-in / focus-navigable) rebinds *how* an
/// action is triggered without touching *what* it means.
///
/// This is the foundation set — the actions needed to operate the current editor on every device.
/// Deferred actions (open-menu, confirm/cancel for a modal pop-in) are named in the design doc but
/// intentionally not added here, to avoid speculative bindings. <see cref="AssignBehavior"/> (design #8049
/// M4) is the one deferred action this milestone reaches.
/// </summary>
public enum EditorAction
{
    /// <summary>Move the canvas grid cursor one cell up (−Y).</summary>
    MoveCursorUp,

    /// <summary>Move the canvas grid cursor one cell down (+Y).</summary>
    MoveCursorDown,

    /// <summary>Move the canvas grid cursor one cell left (−X).</summary>
    MoveCursorLeft,

    /// <summary>Move the canvas grid cursor one cell right (+X).</summary>
    MoveCursorRight,

    /// <summary>Primary action at the cursor — apply the active tool (parity with a mouse left-click).</summary>
    Paint,

    /// <summary>Erase the cell at the cursor, regardless of the active tool (a device convenience).</summary>
    Erase,

    /// <summary>Select the previous tile in the palette (wraps).</summary>
    CycleTilePrev,

    /// <summary>Select the next tile in the palette (wraps).</summary>
    CycleTileNext,

    /// <summary>Select the previous layer (wraps).</summary>
    CycleLayerPrev,

    /// <summary>Select the next layer (wraps).</summary>
    CycleLayerNext,

    /// <summary>Toggle the active tool between paint and erase.</summary>
    ToggleTool,

    /// <summary>Undo the last edit.</summary>
    Undo,

    /// <summary>Redo the last undone edit.</summary>
    Redo,

    /// <summary>Save the level to its current file.</summary>
    Save,

    /// <summary>Move keyboard/gamepad focus to the next surface (canvas ⇄ panels ⇄ toolbar).</summary>
    FocusNext,

    /// <summary>Hold to reveal the tile-palette radial (release/confirm to pick a tile).</summary>
    OpenTileMenu,

    /// <summary>Hold to reveal the layer radial (release/confirm to pick a layer).</summary>
    OpenLayerMenu,

    /// <summary>Hold to reveal the actions radial (file ops, undo/redo, tool toggle).</summary>
    OpenActionMenu,

    /// <summary>Hold (mouse right-button) to reveal the Tiles menu (tap instead erases the cell under the pointer).</summary>
    OpenContextMenu,

    /// <summary>Launch a playtest of the level currently being edited (its in-memory buffer, not the
    /// last-saved file). Returning to the editor is the engine's <c>ui_cancel</c>, not a named editor
    /// action — it is not part of the editor's own input surface.</summary>
    Playtest,

    /// <summary>Step the editor viewport's fixed zoom in one level (DiVoid #7576 — the editor no longer
    /// auto-fits the level to the screen, so zoom needs its own explicit control).</summary>
    ZoomIn,

    /// <summary>Step the editor viewport's fixed zoom out one level.</summary>
    ZoomOut,

    /// <summary>Open the behavior assignment picker for the scriptable subject at the grid cursor (design #8049 M4).</summary>
    AssignBehavior,
}
