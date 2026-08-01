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
    /// The level editor's composition root and controller. It builds the whole <see cref="Control"/>-based
    /// UI (toolbar, layer selector, tile palette, canvas), owns the engine-agnostic
    /// <see cref="LevelEditSession"/>, and translates UI intent into session calls — then reflects the
    /// returned <see cref="CellChange"/> on the canvas. All edit logic and the load/save round-trip live in
    /// the session and the <c>Uberkarl.Editor</c> library; this class is glue: input, layout, and file IO.
    /// </summary>
    public partial class LevelEditor : Control {

        enum Tool { Paint, Erase }

        const string SamplePackagePath = "res://content/sample.pkg";
        const int NewLevelTileSize = 16;
        const int NewLevelWidth = 24;
        const int NewLevelHeight = 16;

        LevelEditSession session;
        Tool activeTool = Tool.Paint;
        int activeTileId = LayerDefinition.EmptyCell;
        int activePaletteIndex = -1;
        int activeLayerIndex;
        string currentFilePath;

        EditorCanvas canvas;
        ItemList layerList;
        ItemList paletteList;
        readonly List<int> paletteTileIds = new List<int>();

        Button saveButton;
        Button undoButton;
        Button redoButton;
        Button paintButton;
        Button eraseButton;
        Label statusLabel;
        FileDialog openDialog;
        FileDialog saveDialog;

        public override void _Ready() {
            Theme = EditorTheme.Build();
            SetAnchorsPreset(LayoutPreset.FullRect);
            BuildUi();

            if (Godot.FileAccess.FileExists(SamplePackagePath))
                LoadFromResPath(SamplePackagePath);
            else
                NewLevel();

            // Start with the canvas focused so a gamepad or keyboard drives the grid cursor immediately,
            // with no click required. Deferred so the whole UI tree is inside the scene tree first.
            canvas.CallDeferred(Control.MethodName.GrabFocus);
        }

        // Global editor actions that work regardless of which surface holds focus. Cursor movement and
        // paint/erase-at-cursor are consumed by the focused EditorCanvas (see EditorCanvas._GuiInput /
        // _Process); everything here is device-neutral and reaches _UnhandledInput because no focused
        // Control claimed it. Guarded against key-repeat echo so a held key fires each action once.
        public override void _UnhandledInput(InputEvent @event) {
            if (@event.IsEcho())
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
            AddChild(background);

            MarginContainer margin = new MarginContainer();
            margin.SetAnchorsPreset(LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            AddChild(margin);

            VBoxContainer root = new VBoxContainer();
            margin.AddChild(root);

            root.AddChild(BuildToolbar());
            root.AddChild(BuildBody());
        }

        Control BuildToolbar() {
            PanelContainer bar = new PanelContainer();
            HBoxContainer row = new HBoxContainer();
            bar.AddChild(row);

            row.AddChild(MakeButton("New", NewLevel));
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

            BuildFileDialogs();
            return bar;
        }

        Control BuildBody() {
            HBoxContainer body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };

            PanelContainer leftPanel = new PanelContainer {
                CustomMinimumSize = new Vector2(220, 0),
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            VBoxContainer leftColumn = new VBoxContainer();
            leftPanel.AddChild(leftColumn);

            leftColumn.AddChild(MakeHeading("Layers"));
            layerList = new ItemList {
                CustomMinimumSize = new Vector2(0, 110),
                SizeFlagsVertical = SizeFlags.Fill,
                AllowReselect = true,
            };
            layerList.ItemSelected += OnLayerSelected;
            leftColumn.AddChild(layerList);

            leftColumn.AddChild(MakeHeading("Tiles"));
            paletteList = new ItemList {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                IconMode = ItemList.IconModeEnum.Top,
                FixedIconSize = new Vector2I(40, 40),
                MaxColumns = 0,
                SameColumnWidth = true,
                AllowReselect = true,
            };
            paletteList.ItemSelected += OnPaletteSelected;
            leftColumn.AddChild(paletteList);

            body.AddChild(leftPanel);

            canvas = new EditorCanvas {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            canvas.CellPressed += OnCellPressed;
            canvas.CellErased += OnCellErased;
            body.AddChild(canvas);

            return body;
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

        void PopulatePalette(EditableLevel level) {
            paletteList.Clear();
            paletteTileIds.Clear();
            foreach (EditableTile tile in level.Tiles) {
                ImageTexture texture = LoadTexture(tile.Graphic);
                int index = texture != null
                    ? paletteList.AddIconItem(texture)
                    : paletteList.AddItem($"#{tile.Id}");
                paletteList.SetItemTooltip(index, $"Tile {tile.Id}{(tile.Collides ? " (solid)" : string.Empty)}");
                paletteTileIds.Add(tile.Id);
            }

            if (paletteTileIds.Count > 0) {
                paletteList.Select(0);
                activePaletteIndex = 0;
                activeTileId = paletteTileIds[0];
            } else {
                activePaletteIndex = -1;
                activeTileId = LayerDefinition.EmptyCell;
            }
        }

        void PopulateLayers(EditableLevel level) {
            layerList.Clear();
            for (int i = 0; i < level.Layers.Count; i++) {
                EditableLayer layer = level.Layers[i];
                string suffix = layer.Collision ? "  [solid]" : string.Empty;
                layerList.AddItem($"{layer.Name}{suffix}");
            }

            activeLayerIndex = 0;
            if (level.Layers.Count > 0)
                layerList.Select(0);
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
            paletteList.Select(next);
            OnPaletteSelected(next); // reuses the mouse path: sets active tile + switches to Paint
        }

        void CycleLayer(int direction) {
            if (session == null || session.Level.Layers.Count == 0)
                return;
            int next = direction >= 0
                ? CyclicSelection.Next(activeLayerIndex, session.Level.Layers.Count)
                : CyclicSelection.Prev(activeLayerIndex, session.Level.Layers.Count);
            layerList.Select(next);
            OnLayerSelected(next);
        }

        void ToggleTool() {
            SetTool(activeTool == Tool.Paint ? Tool.Erase : Tool.Paint);
            paintButton.ButtonPressed = activeTool == Tool.Paint;
            eraseButton.ButtonPressed = activeTool == Tool.Erase;
        }

        // Move focus across the toolbar / panels / canvas so a gamepad or keyboard can reach every
        // control. Godot's native focus navigation walks the visible controls; from the canvas this steps
        // into the panels, and Tab / Shift+Tab (ui_focus_next/prev) cover the reverse on keyboard.
        void AdvanceFocus() {
            Control focused = GetViewport().GuiGetFocusOwner();
            Control next = focused?.FindNextValidFocus();
            (next ?? canvas)?.GrabFocus();
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

        static Label MakeHeading(string text) {
            Label label = new Label { Text = text };
            label.AddThemeColorOverride("font_color", EditorTheme.TextDim);
            return label;
        }
    }
}
