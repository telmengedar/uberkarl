namespace Uberkarl.Editor.Input;

/// <summary>
/// Builds the editor's menus as pure data from primitive editor state — no Godot type, no selection state,
/// no I/O. The controller (<c>game/Editor/LevelEditor.cs</c>) supplies the state and routes the returned
/// <see cref="MenuModel"/> to a surface.
/// </summary>
public static class MenuCatalog
{
    /// <summary>The most entries a radial can render.</summary>
    public const int RadialCap = 8;

    /// <summary>
    /// The Tiles menu: every palette tile, then every terrain, then every object type, as one flat entry
    /// list in three segments. Each entry's outcome carries an index local to its own segment (<see cref="MenuOutcome.SelectTile"/>,
    /// <see cref="MenuOutcome.SelectTerrain"/>, <see cref="MenuOutcome.SelectObjectType"/> each restart at 0).
    /// </summary>
    public static MenuModel BuildTilesMenu(
        IReadOnlyList<int> paletteTileIds,
        IReadOnlyList<string> paletteTerrainLabels,
        IReadOnlyList<string> objectTypeLabels)
    {
        List<MenuItem> items = new List<MenuItem>(paletteTileIds.Count + paletteTerrainLabels.Count + objectTypeLabels.Count);

        for (int i = 0; i < paletteTileIds.Count; i++)
            items.Add(new MenuItem($"#{paletteTileIds[i]}", MenuOutcome.SelectTile(i)));

        for (int i = 0; i < paletteTerrainLabels.Count; i++)
            items.Add(new MenuItem($"Terrain: {paletteTerrainLabels[i]}", MenuOutcome.SelectTerrain(i)));

        for (int i = 0; i < objectTypeLabels.Count; i++)
            items.Add(new MenuItem($"Object: {objectTypeLabels[i]}", MenuOutcome.SelectObjectType(i)));

        return new MenuModel("Tiles", items);
    }

    /// <summary>The Layers menu: one entry per layer, plus a trailing "Manage…" that opens the layer manager.</summary>
    public static MenuModel BuildLayersMenu(IReadOnlyList<string> layerNames)
    {
        List<MenuItem> items = new List<MenuItem>(layerNames.Count + 1);

        for (int i = 0; i < layerNames.Count; i++)
            items.Add(new MenuItem(layerNames[i], MenuOutcome.SelectLayer(i)));

        items.Add(new MenuItem("Manage…", MenuOutcome.OpenLayerManager()));
        return new MenuModel("Layers", items);
    }

    /// <summary>The Actions menu: file ops, undo/redo, tool toggle, and a trailing "More…" that opens <see cref="BuildActionsOverflowMenu"/>. Takes no content — fixed in code, and fits <see cref="RadialCap"/>.</summary>
    public static MenuModel BuildActionsMenu()
    {
        MenuItem[] items =
        {
            new MenuItem("Open", MenuOutcome.FileOp(EditorFileCommand.Open)),
            new MenuItem("Save", MenuOutcome.FileOp(EditorFileCommand.Save)),
            new MenuItem("Undo", MenuOutcome.Invoke(EditorAction.Undo)),
            new MenuItem("Redo", MenuOutcome.Invoke(EditorAction.Redo)),
            new MenuItem("Tool", MenuOutcome.Invoke(EditorAction.ToggleTool)),
            new MenuItem("Play", MenuOutcome.Invoke(EditorAction.Playtest)),
            new MenuItem("More…", MenuOutcome.OpenActionsOverflow()),
        };
        return new MenuModel("Actions", items);
    }

    /// <summary>The Actions overflow list: New, Save As, Resize…, Edit Tileset…, Bind Tileset… — reached through <see cref="BuildActionsMenu"/>'s "More…" entry and rendered on the list surface.</summary>
    public static MenuModel BuildActionsOverflowMenu()
    {
        MenuItem[] items =
        {
            new MenuItem("New", MenuOutcome.FileOp(EditorFileCommand.New)),
            new MenuItem("Save As", MenuOutcome.FileOp(EditorFileCommand.SaveAs)),
            new MenuItem("Resize…", MenuOutcome.OpenResizePanel()),
            new MenuItem("Edit Tileset…", MenuOutcome.OpenTileSetEditor()),
            new MenuItem("Bind Tileset…", MenuOutcome.OpenTileSetBindPanel()),
        };
        return new MenuModel("More", items);
    }

    /// <summary>The radial surface's entry-point guard: throws rather than rendering or truncating <paramref name="menu"/> if it carries more than <see cref="RadialCap"/> entries.</summary>
    /// <exception cref="System.ArgumentException"><paramref name="menu"/> has more than <see cref="RadialCap"/> entries.</exception>
    public static void EnforceRadialCap(MenuModel menu)
    {
        if (menu.Count > RadialCap)
            throw new System.ArgumentException(
                $"'{menu.Title}' has {menu.Count} entries, exceeding MenuCatalog.RadialCap ({RadialCap}). The radial surface refuses to render it.",
                nameof(menu));
    }
}
