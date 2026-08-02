using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Uberkarl.Content;
using Uberkarl.Editor;
using Uberkarl.Editor.Input;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// The level editor's composition root and controller. It owns the engine-agnostic
    /// <see cref="LevelEditSession"/> and translates UI intent into session calls — then reflects the
    /// returned <see cref="CellChange"/> on the canvas. The interaction paradigm is <b>pop-in / hold-to-
    /// reveal</b>: the whole area is the edit canvas, and the palette/layer/action surfaces appear only
    /// while a trigger is held (a radial menu on gamepad/keyboard/mouse), while the toolbar and the
    /// layer/tile panel auto-hide and edge-reveal for the mouse and reveal on focus for gamepad/keyboard.
    /// Every menu choice is routed as a device-neutral <see cref="MenuOutcome"/> onto the editor's existing
    /// operations — the pop-in is a new front-end, not new edit logic. All edit logic and the load/save
    /// round-trip live in the session and the <c>Uberkarl.Editor</c> library; this class is glue: input,
    /// layout, and file IO.
    /// </summary>
    public partial class LevelEditor : Control {

        enum Tool { Paint, Erase }

        // Which pop-in menu, if any, is currently open. One at a time; the owning trigger commits on release.
        enum Trigger { None, Tiles, Layers, Actions, Context }

        // Where gamepad/keyboard focus rests, so the focus action can cycle canvas ⇄ toolbar and reveal the
        // toolbar it lands on (the mouse reveals by edge-hover instead). The tile/layer side panel is gone —
        // it duplicated the Tiles (LB) / Layers (RB) radials, which own tile/layer selection now.
        enum FocusZone { Canvas, Toolbar }

        const string SamplePackagePath = "res://content/sample.pkg";
        const int NewLevelTileSize = 16;
        const int NewLevelWidth = 24;
        const int NewLevelHeight = 16;

        // Press-vs-hold discriminator: a press shorter than this is a tap; longer opens the radial.
        const float HoldThreshold = 0.22f;
        // Edge-reveal hot-zone extent; also the auto-hidden toolbar's height.
        const float TopBarHeight = 48f;

        LevelEditSession session;
        Tool activeTool = Tool.Paint;
        int activeTileId = LayerDefinition.EmptyCell;
        int activePaletteIndex = -1;
        int activeLayerIndex;
        string currentFilePath;

        EditorCanvas canvas;
        Control topBar;
        PopInMenu popIn;
        // Tile/layer selection STATE persists here (the radials read it); the visible side-panel lists that
        // used to mirror it are gone — the Tiles (LB) / Layers (RB) radials fully cover selection.
        readonly List<int> paletteTileIds = new List<int>();
        readonly List<Texture2D> paletteTextures = new List<Texture2D>();

        Button saveButton;
        Button undoButton;
        Button redoButton;
        Button paintButton;
        Button eraseButton;
        Button firstToolButton;
        Label statusLabel;
        FileDialog openDialog;
        FileDialog saveDialog;

        HoldWatch tilesTrigger;
        HoldWatch layersTrigger;
        HoldWatch actionsTrigger;
        HoldWatch contextTrigger;
        Trigger activeTrigger = Trigger.None;
        Vector2 menuCenterGlobal;
        FocusZone focusZone = FocusZone.Canvas;

        public override void _Ready() {
            Theme = EditorTheme.Build();
            SetAnchorsPreset(LayoutPreset.FullRect);

            tilesTrigger = new HoldWatch(HoldThreshold);
            layersTrigger = new HoldWatch(HoldThreshold);
            actionsTrigger = new HoldWatch(HoldThreshold);
            contextTrigger = new HoldWatch(HoldThreshold);

            BuildUi();

            if (Godot.FileAccess.FileExists(SamplePackagePath))
                LoadFromResPath(SamplePackagePath);
            else
                NewLevel();

            // Start with the canvas focused so a gamepad or keyboard drives the grid cursor immediately,
            // with no click required. Deferred so the whole UI tree is inside the scene tree first.
            canvas.CallDeferred(Control.MethodName.GrabFocus);
        }

        // Drives the pop-in triggers (press-vs-hold), feeds the open radial its aim direction, and keeps the
        // auto-hide panels revealed/hidden. Cursor movement and paint/erase-at-cursor are consumed by the
        // focused EditorCanvas; global one-shot actions arrive in _UnhandledInput.
        public override void _Process(double delta) {
            if (popIn == null)
                return;

            float d = (float)delta;
            UpdateReveals();

            // Freeze the grid cursor whenever a radial owns directional input, OR a toolbar/panel focus-zone
            // is active (gamepad/keyboard is navigating it) — independent of which control momentarily holds
            // focus, so the cursor can never step underneath an open menu or a focused panel.
            canvas.DirectionalInputCaptured =
                CursorInputGate.DirectionCaptured(popIn.IsOpen, focusZone != FocusZone.Canvas);

            tilesTrigger.Update(Godot.Input.IsActionPressed(ActionName(EditorAction.OpenTileMenu)), d);
            layersTrigger.Update(Godot.Input.IsActionPressed(ActionName(EditorAction.OpenLayerMenu)), d);
            actionsTrigger.Update(Godot.Input.IsActionPressed(ActionName(EditorAction.OpenActionMenu)), d);
            contextTrigger.Update(Godot.Input.IsActionPressed(ActionName(EditorAction.OpenContextMenu)), d);

            if (activeTrigger != Trigger.None) {
                popIn.SetAim(CurrentAim());
                if (WatchFor(activeTrigger).JustReleased)
                    popIn.Commit();
                return;
            }

            if (tilesTrigger.JustCrossedHold) OpenMenu(Trigger.Tiles);
            else if (layersTrigger.JustCrossedHold) OpenMenu(Trigger.Layers);
            else if (actionsTrigger.JustCrossedHold) OpenMenu(Trigger.Actions);
            else if (contextTrigger.JustCrossedHold) OpenMenu(Trigger.Context);

            // Mouse right-click TAP erases the cell under the pointer (right-click HOLD opened the context
            // radial above instead) — the press-vs-hold split that lets erase and the context menu coexist.
            if (contextTrigger.ReleasedAsTap)
                canvas.EraseAtGlobal(GetViewport().GetMousePosition());
        }

        Vector2 CurrentAim() {
            if (activeTrigger == Trigger.Context)
                return GetViewport().GetMousePosition() - menuCenterGlobal;

            // Gamepad stick + D-pad + keyboard arrows all resolve through the cursor-move actions.
            return Godot.Input.GetVector(
                ActionName(EditorAction.MoveCursorLeft), ActionName(EditorAction.MoveCursorRight),
                ActionName(EditorAction.MoveCursorUp), ActionName(EditorAction.MoveCursorDown));
        }

        HoldWatch WatchFor(Trigger trigger) => trigger switch {
            Trigger.Tiles => tilesTrigger,
            Trigger.Layers => layersTrigger,
            Trigger.Actions => actionsTrigger,
            Trigger.Context => contextTrigger,
            _ => tilesTrigger,
        };

        static string ActionName(EditorAction action) => EditorActionMap.NameOf(action);

        // ----- pop-in menus -----

        void OpenMenu(Trigger trigger) {
            if (session == null)
                return;

            activeTrigger = trigger;
            switch (trigger) {
                case Trigger.Tiles:
                    menuCenterGlobal = canvas.CursorGlobalCenter();
                    popIn.Open(BuildTilesMenu(), menuCenterGlobal, TileIcon);
                    break;
                case Trigger.Layers:
                    menuCenterGlobal = canvas.CursorGlobalCenter();
                    popIn.Open(BuildLayersMenu(), menuCenterGlobal);
                    break;
                case Trigger.Actions:
                    menuCenterGlobal = canvas.CursorGlobalCenter();
                    popIn.Open(BuildActionsMenu(), menuCenterGlobal);
                    break;
                case Trigger.Context:
                    menuCenterGlobal = GetViewport().GetMousePosition();
                    popIn.Open(BuildTilesMenu(), menuCenterGlobal, TileIcon);
                    break;
            }
        }

        RadialMenuModel BuildTilesMenu() {
            List<RadialMenuItem> items = new List<RadialMenuItem>(paletteTileIds.Count);
            for (int i = 0; i < paletteTileIds.Count; i++)
                items.Add(new RadialMenuItem($"#{paletteTileIds[i]}", MenuOutcome.SelectTile(i)));
            return new RadialMenuModel("Tiles", items);
        }

        RadialMenuModel BuildLayersMenu() {
            List<RadialMenuItem> items = new List<RadialMenuItem>();
            if (session != null) {
                for (int i = 0; i < session.Level.Layers.Count; i++)
                    items.Add(new RadialMenuItem(session.Level.Layers[i].Name, MenuOutcome.SelectLayer(i)));
            }
            return new RadialMenuModel("Layers", items);
        }

        RadialMenuModel BuildActionsMenu() {
            RadialMenuItem[] items = {
                new RadialMenuItem("New", MenuOutcome.FileOp(EditorFileCommand.New)),
                new RadialMenuItem("Open", MenuOutcome.FileOp(EditorFileCommand.Open)),
                new RadialMenuItem("Save", MenuOutcome.FileOp(EditorFileCommand.Save)),
                new RadialMenuItem("Save As", MenuOutcome.FileOp(EditorFileCommand.SaveAs)),
                new RadialMenuItem("Undo", MenuOutcome.Invoke(EditorAction.Undo)),
                new RadialMenuItem("Redo", MenuOutcome.Invoke(EditorAction.Redo)),
                new RadialMenuItem("Tool", MenuOutcome.Invoke(EditorAction.ToggleTool)),
            };
            return new RadialMenuModel("Actions", items);
        }

        Texture2D TileIcon(int paletteIndex) =>
            paletteIndex >= 0 && paletteIndex < paletteTextures.Count ? paletteTextures[paletteIndex] : null;

        // The single routing point: a device-neutral menu outcome onto the editor's existing operations —
        // the same palette/layer selection and undo/redo/save/tool paths the toolbar and hotkeys use.
        void Dispatch(MenuOutcome outcome) {
            switch (outcome.Kind) {
                case MenuOutcomeKind.SelectTile:
                    if (outcome.Index >= 0 && outcome.Index < paletteTileIds.Count)
                        OnPaletteSelected(outcome.Index);
                    break;
                case MenuOutcomeKind.SelectLayer:
                    if (session != null && outcome.Index >= 0 && outcome.Index < session.Level.Layers.Count)
                        OnLayerSelected(outcome.Index);
                    break;
                case MenuOutcomeKind.InvokeAction:
                    InvokeMenuAction(outcome.Action);
                    break;
                case MenuOutcomeKind.FileCommand:
                    InvokeFileCommand(outcome.File);
                    break;
            }
            EndMenu();
        }

        void InvokeMenuAction(EditorAction action) {
            switch (action) {
                case EditorAction.Undo: Undo(); break;
                case EditorAction.Redo: Redo(); break;
                case EditorAction.ToggleTool: ToggleTool(); break;
            }
        }

        void InvokeFileCommand(EditorFileCommand command) {
            switch (command) {
                case EditorFileCommand.New: NewLevel(); break;
                case EditorFileCommand.Open: openDialog.PopupCentered(new Vector2I(720, 480)); break;
                case EditorFileCommand.Save: Save(); break;
                case EditorFileCommand.SaveAs: saveDialog.PopupCentered(new Vector2I(720, 480)); break;
            }
        }

        void OnMenuCancelled() => EndMenu();

        void EndMenu() {
            activeTrigger = Trigger.None;
            focusZone = FocusZone.Canvas;
            canvas?.GrabFocus();
        }

        // ----- auto-hide / edge-reveal -----

        // Keep the whole area as canvas: hide the toolbar by default. Reveal it when (gamepad/keyboard) the
        // focus-zone rests on it — a sticky reveal that survives momentary focus loss while navigating inside
        // it, so a directional press no longer insta-hides it — or when (mouse) the pointer is in its top-edge
        // band or a child of it holds focus. It hides while a radial is open.
        void UpdateReveals() {
            if (topBar == null)
                return;

            bool menuOpen = popIn.IsOpen;
            Vector2 mouse = GetViewport().GetMousePosition();
            Control focus = GetViewport().GuiGetFocusOwner();

            topBar.Visible = !menuOpen &&
                (focusZone == FocusZone.Toolbar || FocusInside(topBar, focus) || mouse.Y <= TopBarHeight);
        }

        static bool FocusInside(Control container, Control focus) =>
            focus != null && (focus == container || container.IsAncestorOf(focus));

        // Global editor actions that work regardless of which surface holds focus. Suppressed while a radial
        // is open (the radial owns input then). Cursor movement and paint/erase-at-cursor are consumed by the
        // focused EditorCanvas; everything here is device-neutral and reaches _UnhandledInput because no
        // focused Control claimed it. Guarded against key-repeat echo so a held key fires each action once.
        public override void _UnhandledInput(InputEvent @event) {
            if (@event.IsEcho() || (popIn != null && popIn.IsOpen))
                return;

            if (Fired(@event, EditorAction.CycleTilePrev)) CycleTile(-1);
            else if (Fired(@event, EditorAction.CycleTileNext)) CycleTile(+1);
            else if (Fired(@event, EditorAction.CycleLayerPrev)) CycleLayer(-1);
            else if (Fired(@event, EditorAction.CycleLayerNext)) CycleLayer(+1);
            else if (Fired(@event, EditorAction.ToggleTool)) ToggleTool();
            else if (Fired(@event, EditorAction.Undo)) Undo();
            else if (Fired(@event, EditorAction.Redo)) Redo();
            else if (Fired(@event, EditorAction.Save)) Save();
            else if (Fired(@event, EditorAction.FocusNext)) AdvanceFocus();
            else return;

            GetViewport().SetInputAsHandled();
        }

        static bool Fired(InputEvent @event, EditorAction action)
            => @event.IsActionPressed(EditorActionMap.NameOf(action));

        // ----- UI construction -----

        void BuildUi() {
            ColorRect background = new ColorRect { Color = EditorTheme.Shell };
            background.SetAnchorsPreset(LayoutPreset.FullRect);
            background.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(background);

            // The canvas fills the whole area — maximum edit surface; the toolbar overlays it and auto-hides.
            canvas = new EditorCanvas();
            canvas.SetAnchorsPreset(LayoutPreset.FullRect);
            canvas.CellPressed += OnCellPressed;
            canvas.CellErased += OnCellErased;
            AddChild(canvas);

            topBar = BuildToolbar();
            topBar.SetAnchorsPreset(LayoutPreset.TopWide);
            topBar.OffsetBottom = TopBarHeight;
            AddChild(topBar);

            popIn = new PopInMenu();
            popIn.Chosen += Dispatch;
            popIn.Cancelled += OnMenuCancelled;
            AddChild(popIn);

            BuildFileDialogs();
        }

        Control BuildToolbar() {
            PanelContainer bar = new PanelContainer();
            HBoxContainer row = new HBoxContainer();
            bar.AddChild(row);

            firstToolButton = MakeButton("New", NewLevel);
            row.AddChild(firstToolButton);
            row.AddChild(MakeButton("Open", () => openDialog.PopupCentered(new Vector2I(720, 480))));
            saveButton = MakeButton("Save", Save);
            row.AddChild(saveButton);
            row.AddChild(MakeButton("Save As", () => saveDialog.PopupCentered(new Vector2I(720, 480))));

            row.AddChild(MakeSeparator());

            ButtonGroup toolGroup = new ButtonGroup();
            paintButton = MakeToggle("Paint", toolGroup, () => SetTool(Tool.Paint));
            paintButton.ButtonPressed = true;
            eraseButton = MakeToggle("Erase", toolGroup, () => SetTool(Tool.Erase));
            row.AddChild(paintButton);
            row.AddChild(eraseButton);

            row.AddChild(MakeSeparator());

            undoButton = MakeButton("Undo", Undo);
            redoButton = MakeButton("Redo", Redo);
            row.AddChild(undoButton);
            row.AddChild(redoButton);

            Control spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(spacer);

            statusLabel = new Label { Text = string.Empty, VerticalAlignment = VerticalAlignment.Center };
            statusLabel.AddThemeColorOverride("font_color", EditorTheme.TextDim);
            row.AddChild(statusLabel);

            ContainToolbarFocus(row);
            return bar;
        }

        // Keep gamepad/keyboard focus fully inside the toolbar once B lands there. A real stick/D-pad press
        // fires Godot's built-in ui_left/right/up/down focus navigation ALONGSIDE the editor cursor action
        // (both are bound to the same D-pad + left stick), so any focus neighbour left unset falls back to
        // Godot's geometric search — and because the edit canvas is a focusable full-rect Control underlying
        // the whole viewport, that search selects the canvas and strands focus there (the round-1 fix pinned
        // only the vertical + row-end sides and left the middle buttons' left/right geometry-driven, which on
        // a real pad still escaped). So wire EVERY side of EVERY button to a sibling in the row — the ends pin
        // to self — leaving no geometric side that can reach the canvas. Next/previous pin to self too so a
        // keyboard Tab's ui_focus_next cannot escape either; the editor focus-next action still cycles zones.
        static void ContainToolbarFocus(Container row) {
            NodePath self = new NodePath(".");
            List<Button> buttons = new List<Button>();
            foreach (Node child in row.GetChildren())
                if (child is Button button)
                    buttons.Add(button);

            for (int i = 0; i < buttons.Count; i++) {
                Button button = buttons[i];
                button.FocusNeighborTop = self;
                button.FocusNeighborBottom = self;
                button.FocusNeighborLeft = i > 0 ? button.GetPathTo(buttons[i - 1]) : self;
                button.FocusNeighborRight = i < buttons.Count - 1 ? button.GetPathTo(buttons[i + 1]) : self;
                button.FocusNext = self;
                button.FocusPrevious = self;
            }
        }

        void BuildFileDialogs() {
            openDialog = new FileDialog {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Title = "Open level package",
                Filters = new[] { "*.pkg ; Uberkarl package" },
            };
            openDialog.FileSelected += OnOpenFileSelected;
            AddChild(openDialog);

            saveDialog = new FileDialog {
                FileMode = FileDialog.FileModeEnum.SaveFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Title = "Save level package",
                Filters = new[] { "*.pkg ; Uberkarl package" },
            };
            saveDialog.FileSelected += OnSaveFileSelected;
            AddChild(saveDialog);

            string contentDir = ProjectSettings.GlobalizePath("res://content");
            if (DirAccess.DirExistsAbsolute(contentDir)) {
                openDialog.CurrentDir = contentDir;
                saveDialog.CurrentDir = contentDir;
            }
        }

        // ----- session lifecycle -----

        void NewLevel() {
            EditableLevel level = EditableLevel.CreateBlank(
                "Untitled", NewLevelTileSize, NewLevelWidth, NewLevelHeight, DefaultPalette.Build(NewLevelTileSize));
            currentFilePath = null;
            AdoptSession(level);
            GD.Print($"LevelEditor: new blank {NewLevelWidth}x{NewLevelHeight} level.");
        }

        void LoadFromResPath(string resPath) {
            byte[] bytes = Godot.FileAccess.GetFileAsBytes(resPath);
            if (bytes == null || bytes.Length == 0) {
                GD.PrintErr($"LevelEditor: package '{resPath}' is missing or empty.");
                return;
            }
            LoadFromBytes(bytes, ProjectSettings.GlobalizePath(resPath));
        }

        void LoadFromAbsolutePath(string absolutePath) {
            try {
                byte[] bytes = File.ReadAllBytes(absolutePath);
                LoadFromBytes(bytes, absolutePath);
            } catch (Exception exception) {
                GD.PrintErr($"LevelEditor: failed to read '{absolutePath}': {exception.GetType().Name}: {exception.Message}");
            }
        }

        void LoadFromBytes(byte[] bytes, string sourcePath) {
            try {
                EditableLevel level = EditableLevelReader.FromPackageBytes(bytes);
                currentFilePath = sourcePath;
                AdoptSession(level);
                GD.Print($"LevelEditor: loaded {level.Width}x{level.Height} level '{level.Name}' " +
                    $"({level.Tiles.Count} tiles, {level.Layers.Count} layers) from {sourcePath}.");
            } catch (Exception exception) {
                GD.PrintErr($"LevelEditor: {exception.GetType().Name}: {exception.Message}");
            }
        }

        void AdoptSession(EditableLevel level) {
            session = new LevelEditSession(level);
            canvas.SetLevel(EditableLevelSnapshot.ToResolvedLevel(level));
            PopulatePalette(level);
            PopulateLayers(level);
            SetTool(Tool.Paint);
            paintButton.ButtonPressed = true;
            UpdateState();
        }

        // Rebuild the tile-selection STATE the Tiles radial reads: the ordered tile ids and their textures
        // (the radial draws these as wedge icons). No visible list any more — the side panel is gone.
        void PopulatePalette(EditableLevel level) {
            paletteTileIds.Clear();
            paletteTextures.Clear();
            foreach (EditableTile tile in level.Tiles) {
                paletteTileIds.Add(tile.Id);
                paletteTextures.Add(LoadTexture(tile.Graphic));
            }

            if (paletteTileIds.Count > 0) {
                activePaletteIndex = 0;
                activeTileId = paletteTileIds[0];
            } else {
                activePaletteIndex = -1;
                activeTileId = LayerDefinition.EmptyCell;
            }
        }

        // Reset the active layer to the first. Layer names are read live from the session by the Layers radial
        // (BuildLayersMenu), so there is no list to populate here — only the selection state.
        void PopulateLayers(EditableLevel level) {
            activeLayerIndex = 0;
        }

        static ImageTexture LoadTexture(byte[] png) {
            Image image = new Image();
            if (image.LoadPngFromBuffer(png) != Error.Ok)
                return null;
            return ImageTexture.CreateFromImage(image);
        }

        // ----- edit actions -----

        void OnCellPressed(int x, int y) {
            if (session == null)
                return;

            CellChange? change = activeTool == Tool.Erase
                ? session.EraseCell(activeLayerIndex, x, y)
                : activeTileId != LayerDefinition.EmptyCell
                    ? session.PaintCell(activeLayerIndex, x, y, activeTileId)
                    : null;

            if (change is { } committed)
                canvas.Apply(committed);
            UpdateState();
        }

        void OnCellErased(int x, int y) {
            if (session == null)
                return;
            if (session.EraseCell(activeLayerIndex, x, y) is { } committed)
                canvas.Apply(committed);
            UpdateState();
        }

        // ----- action-driven navigation (gamepad + keyboard parity with mouse selection) -----

        void CycleTile(int direction) {
            if (paletteTileIds.Count == 0)
                return;
            int next = direction >= 0
                ? CyclicSelection.Next(activePaletteIndex, paletteTileIds.Count)
                : CyclicSelection.Prev(activePaletteIndex, paletteTileIds.Count);
            OnPaletteSelected(next); // sets active tile + switches to Paint (same path the radial pick uses)
        }

        void CycleLayer(int direction) {
            if (session == null || session.Level.Layers.Count == 0)
                return;
            int next = direction >= 0
                ? CyclicSelection.Next(activeLayerIndex, session.Level.Layers.Count)
                : CyclicSelection.Prev(activeLayerIndex, session.Level.Layers.Count);
            OnLayerSelected(next);
        }

        void ToggleTool() {
            SetTool(activeTool == Tool.Paint ? Tool.Erase : Tool.Paint);
            paintButton.ButtonPressed = activeTool == Tool.Paint;
            eraseButton.ButtonPressed = activeTool == Tool.Erase;
        }

        // Toggle gamepad/keyboard focus between the canvas and the toolbar, revealing the toolbar when it
        // lands there so it becomes reachable without a mouse (the mouse reveals by edge-hover instead). Tile
        // and layer selection are no longer a focus zone — the Tiles (LB) / Layers (RB) radials own them.
        void AdvanceFocus() {
            focusZone = focusZone == FocusZone.Canvas ? FocusZone.Toolbar : FocusZone.Canvas;

            if (focusZone == FocusZone.Toolbar) {
                topBar.Visible = true;
                firstToolButton?.GrabFocus();
            } else {
                canvas.GrabFocus();
            }
        }

        void Undo() {
            if (session?.Undo() is { } change) {
                canvas.Apply(change);
                UpdateState();
            }
        }

        void Redo() {
            if (session?.Redo() is { } change) {
                canvas.Apply(change);
                UpdateState();
            }
        }

        void Save() {
            if (currentFilePath == null)
                saveDialog.PopupCentered(new Vector2I(720, 480));
            else
                WriteToPath(currentFilePath);
        }

        void WriteToPath(string absolutePath) {
            if (session == null)
                return;

            try {
                byte[] bytes = session.Save();
                File.WriteAllBytes(absolutePath, bytes);
                currentFilePath = absolutePath;
                GD.Print($"LevelEditor: saved {bytes.Length} bytes to {absolutePath}.");
            } catch (Exception exception) {
                session.MarkDirty();
                GD.PrintErr($"LevelEditor: save failed: {exception.GetType().Name}: {exception.Message}");
            }

            UpdateState();
        }

        // ----- signal handlers -----

        void OnPaletteSelected(long index) {
            int i = (int)index;
            if (i >= 0 && i < paletteTileIds.Count) {
                activePaletteIndex = i;
                activeTileId = paletteTileIds[i];
            }
            SetTool(Tool.Paint);
            paintButton.ButtonPressed = true;
            eraseButton.ButtonPressed = false;
            UpdateState();
        }

        void OnLayerSelected(long index) {
            activeLayerIndex = (int)index;
            UpdateState();
        }

        void OnOpenFileSelected(string path) => LoadFromAbsolutePath(path);

        void OnSaveFileSelected(string path) {
            if (!path.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
                path += ".pkg";
            WriteToPath(path);
        }

        void SetTool(Tool tool) {
            activeTool = tool;
            UpdateState();
        }

        // ----- presentation -----

        void UpdateState() {
            if (session != null) {
                undoButton.Disabled = !session.CanUndo;
                redoButton.Disabled = !session.CanRedo;
            }

            statusLabel.Text = BuildStatusText();
        }

        string BuildStatusText() {
            if (session == null)
                return string.Empty;

            string file = currentFilePath == null ? "unsaved" : Path.GetFileName(currentFilePath);
            string dirty = session.IsDirty ? " *" : string.Empty;
            string layer = activeLayerIndex >= 0 && activeLayerIndex < session.Level.Layers.Count
                ? session.Level.Layers[activeLayerIndex].Name
                : "-";
            string tile = activeTool == Tool.Erase
                ? "erase"
                : activeTileId == LayerDefinition.EmptyCell ? "none" : $"#{activeTileId}";
            return $"{session.Level.Name}{dirty}  ·  {file}  ·  layer: {layer}  ·  tool: {activeTool} ({tile})";
        }

        // ----- small factory helpers -----

        Button MakeButton(string text, Action onPressed) {
            Button button = new Button { Text = text };
            button.Pressed += onPressed;
            return button;
        }

        Button MakeToggle(string text, ButtonGroup group, Action onPressed) {
            Button button = new Button { Text = text, ToggleMode = true, ButtonGroup = group };
            button.Pressed += onPressed;
            return button;
        }

        static Control MakeSeparator() {
            VSeparator separator = new VSeparator();
            return separator;
        }
    }
}
