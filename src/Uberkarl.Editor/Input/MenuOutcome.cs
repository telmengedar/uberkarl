namespace Uberkarl.Editor.Input;

/// <summary>What kind of thing a chosen radial-menu wedge does.</summary>
public enum MenuOutcomeKind
{
    /// <summary>Select a tile in the palette by its index.</summary>
    SelectTile,

    /// <summary>Select a layer by its index.</summary>
    SelectLayer,

    /// <summary>Invoke a named editor action (undo/redo, tool change, …).</summary>
    InvokeAction,

    /// <summary>Run a file-lifecycle command (new/open/save/save-as).</summary>
    FileCommand,

    /// <summary>Summon the layer-management panel (create/delete/reorder/property-edit layers).</summary>
    OpenLayerManager,

    /// <summary>Summon the level-resize panel (set width/height — DiVoid #7550).</summary>
    OpenResizePanel,

    /// <summary>Summon the tile set editor (add/remove/rename tiles, import graphics — DiVoid #7551).</summary>
    OpenTileSetEditor,

    /// <summary>Summon the "bind a different shared tile set" panel (DiVoid #7551).</summary>
    OpenTileSetBindPanel,
}

/// <summary>The file-lifecycle commands a menu can request; the controller maps these to its file IO.</summary>
public enum EditorFileCommand
{
    New,
    Open,
    Save,
    SaveAs,
}

/// <summary>
/// The device-neutral result of choosing a radial-menu wedge — <b>what should happen</b>, not how. It
/// deliberately carries no Godot type and no callback: the pop-in surface produces one of these, and the
/// controller dispatches it onto the editor's <em>existing</em> operations (palette/layer selection, the
/// same undo/redo/save/tool paths the toolbar uses). This is the seam that keeps the pop-in a pure
/// front-end: menus decide intent, the controller owns the wiring, and the mapping from wedge to intent is
/// unit-tested here without the engine.
/// </summary>
public readonly struct MenuOutcome
{
    private MenuOutcome(MenuOutcomeKind kind, int index, EditorAction action, EditorFileCommand file)
    {
        Kind = kind;
        Index = index;
        Action = action;
        File = file;
    }

    /// <summary>Which category of outcome this is; selects which payload is meaningful.</summary>
    public MenuOutcomeKind Kind { get; }

    /// <summary>The tile or layer index — meaningful for <see cref="MenuOutcomeKind.SelectTile"/> / <see cref="MenuOutcomeKind.SelectLayer"/>.</summary>
    public int Index { get; }

    /// <summary>The editor action — meaningful for <see cref="MenuOutcomeKind.InvokeAction"/>.</summary>
    public EditorAction Action { get; }

    /// <summary>The file command — meaningful for <see cref="MenuOutcomeKind.FileCommand"/>.</summary>
    public EditorFileCommand File { get; }

    /// <summary>An outcome that selects the palette tile at <paramref name="index"/>.</summary>
    public static MenuOutcome SelectTile(int index) =>
        new(MenuOutcomeKind.SelectTile, index, default, default);

    /// <summary>An outcome that selects the layer at <paramref name="index"/>.</summary>
    public static MenuOutcome SelectLayer(int index) =>
        new(MenuOutcomeKind.SelectLayer, index, default, default);

    /// <summary>An outcome that invokes editor <paramref name="action"/> (e.g. undo, redo, tool change).</summary>
    public static MenuOutcome Invoke(EditorAction action) =>
        new(MenuOutcomeKind.InvokeAction, -1, action, default);

    /// <summary>An outcome that runs file <paramref name="command"/> (new/open/save/save-as).</summary>
    public static MenuOutcome FileOp(EditorFileCommand command) =>
        new(MenuOutcomeKind.FileCommand, -1, default, command);

    /// <summary>An outcome that summons the layer-management panel. No new <see cref="EditorAction"/> is
    /// introduced for this — the "Manage…" wedge rides the existing Layers radial trigger.</summary>
    public static MenuOutcome OpenLayerManager() =>
        new(MenuOutcomeKind.OpenLayerManager, -1, default, default);

    /// <summary>An outcome that summons the level-resize panel. No new <see cref="EditorAction"/> is
    /// introduced for this either — the "Resize…" wedge rides the existing Actions radial trigger.</summary>
    public static MenuOutcome OpenResizePanel() =>
        new(MenuOutcomeKind.OpenResizePanel, -1, default, default);

    /// <summary>An outcome that summons the tile set editor. Rides the existing Actions radial trigger.</summary>
    public static MenuOutcome OpenTileSetEditor() =>
        new(MenuOutcomeKind.OpenTileSetEditor, -1, default, default);

    /// <summary>An outcome that summons the tile-set bind panel. Rides the existing Actions radial trigger.</summary>
    public static MenuOutcome OpenTileSetBindPanel() =>
        new(MenuOutcomeKind.OpenTileSetBindPanel, -1, default, default);
}
