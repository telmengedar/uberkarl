namespace Uberkarl.Editor.Input;

/// <summary>
/// The single, authoritative binding between the device-neutral <see cref="EditorAction"/> intents and
/// the stable string names the engine's input map keys its device bindings on. The engine layer never
/// hard-codes an action-name string: it asks this map, so the set of actions and their names is defined
/// in exactly one engine-agnostic place and can be unit-tested for completeness and uniqueness. The
/// device-to-name bindings themselves (which key / button / stick triggers <c>"editor_cursor_up"</c>)
/// live in the engine's input configuration — this map only names the actions, it does not know about
/// devices.
/// </summary>
public static class EditorActionMap
{
    private static readonly IReadOnlyDictionary<EditorAction, string> ByAction = new Dictionary<EditorAction, string>
    {
        [EditorAction.MoveCursorUp] = "editor_cursor_up",
        [EditorAction.MoveCursorDown] = "editor_cursor_down",
        [EditorAction.MoveCursorLeft] = "editor_cursor_left",
        [EditorAction.MoveCursorRight] = "editor_cursor_right",
        [EditorAction.Paint] = "editor_paint",
        [EditorAction.Erase] = "editor_erase",
        [EditorAction.CycleTilePrev] = "editor_cycle_tile_prev",
        [EditorAction.CycleTileNext] = "editor_cycle_tile_next",
        [EditorAction.CycleLayerPrev] = "editor_cycle_layer_prev",
        [EditorAction.CycleLayerNext] = "editor_cycle_layer_next",
        [EditorAction.ToggleTool] = "editor_toggle_tool",
        [EditorAction.Undo] = "editor_undo",
        [EditorAction.Redo] = "editor_redo",
        [EditorAction.Save] = "editor_save",
        [EditorAction.FocusNext] = "editor_focus_next",
        [EditorAction.OpenTileMenu] = "editor_menu_tiles",
        [EditorAction.OpenLayerMenu] = "editor_menu_layers",
        [EditorAction.OpenActionMenu] = "editor_menu_actions",
        [EditorAction.OpenContextMenu] = "editor_menu_context",
        [EditorAction.Playtest] = "editor_playtest",
        [EditorAction.ZoomIn] = "editor_zoom_in",
        [EditorAction.ZoomOut] = "editor_zoom_out",
    };

    private static readonly IReadOnlyDictionary<string, EditorAction> ByName =
        ByAction.ToDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>Every action that must be bound for the editor to be fully operable on every device.</summary>
    public static IReadOnlyCollection<EditorAction> All => (IReadOnlyCollection<EditorAction>)ByAction.Keys;

    /// <summary>The stable input-map name for an action (e.g. <c>"editor_cursor_up"</c>).</summary>
    public static string NameOf(EditorAction action) =>
        ByAction.TryGetValue(action, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(action), action, "No input-map name is bound for this action.");

    /// <summary>Resolves an input-map name back to its action, if it is a known editor action.</summary>
    public static bool TryResolve(string actionName, out EditorAction action) =>
        ByName.TryGetValue(actionName, out action);
}
