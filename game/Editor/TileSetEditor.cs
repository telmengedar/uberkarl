using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Editor;

namespace Uberkarl {

    /// <summary>
    /// The summoned, gamepad-first tile set authoring surface (DiVoid #7551 Phase 1b/2, design #7580):
    /// add/remove/rename simple tiles, toggle full-tile collision, and import a graphic via Godot's
    /// standard <see cref="FileDialog"/>. Reuses <see cref="LayerManagerPanel"/>'s scaffolding verbatim —
    /// full-rect dim backdrop, centered panel, 2D <see cref="FocusGrid"/> row layout, grab-focus-on-summon,
    /// <c>ui_cancel</c> closes — and the same "panel calls the session directly, then rebuilds its own rows
    /// from the model's current truth" pattern. It holds no edit logic of its own.
    ///
    /// <b>Animation</b> (DiVoid #7551 Phase 2): the same "+ Add Frame…" import flow every tile row offers
    /// appends the tile's next animation frame — a tile with 0 extra frames is simple, 1+ is animated
    /// (structural, no separate toggle, design #7580 §7/§10). An animated tile grows a speed row (step
    /// through <see cref="AnimationSpeedLadder"/>) and one row per extra frame with its own "Remove Frame".
    /// Removing the last extra frame reverts the tile to simple.
    ///
    /// <b>Graphic import</b> (design #7580 D1, ratified): Godot's built-in <see cref="FileDialog"/> — now
    /// pad-navigable since PR #13 bound <c>ui_accept</c>/<c>ui_cancel</c> to gamepad A/B, which is what
    /// <see cref="FileDialog"/>'s own internal file-list navigation and Open/Cancel buttons already run on
    /// — rather than a custom in-engine file browser. This is for importing EXTERNAL art into the package;
    /// it does not replace <see cref="PackageBrowser"/>'s package/resource browsing.
    ///
    /// <b>Naming</b> (DiVoid #7513): a tile's header/name cell opens the shared <see cref="OnScreenKeyboard"/>
    /// seeded with its current name, mirroring <see cref="LayerManagerPanel"/>'s rename affordance exactly.
    /// </summary>
    public partial class TileSetEditor : Control {

        TileSetEditSession session;
        VBoxContainer listBox;
        OnScreenKeyboard keyboard;
        FileDialog importDialog;

        int tileSize = 1;

        int pendingDeleteId = -1;

        // Which tile a graphic picked from importDialog becomes: -1 means "a brand-new tile"
        // (OnAddTilePressed's flow, unchanged from Phase 1b); any other value means "the next animation
        // frame for THAT tile id" (DiVoid #7551 Phase 2 — OnAddFramePressed's flow). The dialog itself is
        // one shared instance either way (mirrors the rest of the editor's "build once, reconfigure on
        // summon" convention), so OnGraphicFileSelected branches on this field to know which session call
        // to make once a file is actually picked.
        int pendingFrameTileId = -1;

        // Confirm-gated frame removal (mirrors pendingDeleteId's full-tile confirm): frame edits are not on
        // an undo stack this increment, same as every other tile-set mutation in this panel.
        int pendingRemoveFrameTileId = -1;
        int pendingRemoveFrameIndex = -1;

        int lastFocusedRow;
        int lastFocusedCol;

        /// <summary>Raised after any mutation (add/remove/rename/set-collides): "refresh the canvas + palette + status."</summary>
        public event Action TileSetModelChanged;

        /// <summary>Raised when the panel is dismissed (<c>ui_cancel</c>).</summary>
        public event Action Closed;

        /// <summary>True while the panel is summoned.</summary>
        public bool IsOpen => Visible;

        public override void _Ready() {
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All;
            Visible = false;
            ZIndex = 100;
            BuildLayout();
            BuildImportDialog();
        }

        void BuildLayout() {
            ColorRect backdrop = new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f, 0.75f) };
            backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
            backdrop.MouseFilter = MouseFilterEnum.Stop;
            AddChild(backdrop);

            PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(760f, 420f) };
            panel.SetAnchorsPreset(LayoutPreset.Center);
            AddChild(panel);

            VBoxContainer root = new VBoxContainer();
            panel.AddChild(root);

            Label title = new Label { Text = "Edit Tileset" };
            root.AddChild(title);

            ScrollContainer scroll = new ScrollContainer { CustomMinimumSize = new Vector2(740f, 360f) };
            root.AddChild(scroll);

            listBox = new VBoxContainer();
            scroll.AddChild(listBox);
        }

        // A single reused FileDialog instance (mirrors the rest of the editor's "build once in _Ready,
        // reconfigure on summon" convention). Filesystem access + PNG filter; FileModeEnum.OpenFile is a
        // single-selection open picker. Godot's own ui_accept/ui_cancel bindings (PR #13: gamepad A/B)
        // already drive its internal file list and Open/Cancel buttons — no extra wiring needed here for
        // gamepad support beyond those bindings existing project-wide.
        void BuildImportDialog() {
            importDialog = new FileDialog {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Title = "Import Tile Graphic (PNG)",
                Size = new Vector2I(720, 480),
            };
            importDialog.AddFilter("*.png", "PNG Images");
            importDialog.FileSelected += OnGraphicFileSelected;
            AddChild(importDialog);
        }

        /// <summary>
        /// Attaches the shared <see cref="OnScreenKeyboard"/> the rename affordance summons (DiVoid #7513).
        /// Called once by <see cref="LevelEditor"/> alongside construction, exactly like <see cref="LayerManagerPanel.AttachKeyboard"/>.
        /// </summary>
        public void AttachKeyboard(OnScreenKeyboard onScreenKeyboard) => keyboard = onScreenKeyboard;

        /// <summary>
        /// Summon the panel against <paramref name="editSession"/>, showing its current tiles.
        /// <paramref name="levelTileSize"/> is the level grid's square cell size (pixels) that a newly
        /// imported graphic is scaled to fill (DiVoid #7551 bugfix) — the caller's <c>EditableLevel.TileSize</c>.
        /// </summary>
        public void Summon(TileSetEditSession editSession, int levelTileSize) {
            session = editSession;
            tileSize = levelTileSize;
            pendingDeleteId = -1;
            pendingFrameTileId = -1;
            pendingRemoveFrameTileId = -1;
            pendingRemoveFrameIndex = -1;
            lastFocusedRow = 0;
            lastFocusedCol = 0;
            Visible = true;
            Rebuild();
        }

        void Rebuild() {
            foreach (Node child in listBox.GetChildren())
                child.QueueFree();

            List<List<Control>> rows = new List<List<Control>>();

            Label header = new Label { Text = session != null ? $"“{session.TileSet.Name}” — {session.TileSet.Tiles.Count} tile(s)" : string.Empty };
            header.AddThemeColorOverride("font_color", EditorTheme.Accent);
            listBox.AddChild(header);

            Button addButton = new Button { Text = "+ Add Tile (import PNG)…" };
            addButton.Pressed += OnAddTilePressed;
            listBox.AddChild(addButton);
            rows.Add(new List<Control> { addButton });

            if (session != null) {
                foreach (EditableTile tile in session.TileSet.Tiles) {
                    rows.Add(BuildTileRow(tile));
                    if (tile.IsAnimated) {
                        rows.Add(BuildAnimationSpeedRow(tile));
                        for (int frameIndex = 0; frameIndex < tile.Frames.Count; frameIndex++)
                            rows.Add(BuildFrameRow(tile, frameIndex));
                    }
                }
            }

            FocusGrid.Contain(rows);
            TrackFocusPosition(rows);

            int restoreRow = Math.Clamp(lastFocusedRow, 0, rows.Count - 1);
            int restoreCol = Math.Clamp(lastFocusedCol, 0, rows[restoreRow].Count - 1);
            rows[restoreRow][restoreCol].CallDeferred(Control.MethodName.GrabFocus);
        }

        // Same technique as LayerManagerPanel.TrackFocusPosition: a Rebuild() triggered by a mutation would
        // otherwise always snap focus back to "+ Add Tile".
        void TrackFocusPosition(List<List<Control>> rows) {
            for (int r = 0; r < rows.Count; r++) {
                for (int c = 0; c < rows[r].Count; c++) {
                    int capturedRow = r;
                    int capturedCol = c;
                    rows[r][c].FocusEntered += () => {
                        lastFocusedRow = capturedRow;
                        lastFocusedCol = capturedCol;
                    };
                }
            }
        }

        List<Control> BuildTileRow(EditableTile tile) {
            HBoxContainer row = new HBoxContainer();
            listBox.AddChild(row);

            List<Control> columns = new List<Control>();

            TextureRect thumb = new TextureRect {
                CustomMinimumSize = new Vector2(28f, 28f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Texture = LoadTexture(tile.Graphic),
            };
            row.AddChild(thumb);

            string label = string.IsNullOrEmpty(tile.Name)
                ? $"Tile #{tile.Id}"
                : $"{tile.Name} (#{tile.Id})";
            label = tile.IsAnimated ? $"{label} — animated, {tile.Frames.Count + 1} frames" : label;
            Button header = new Button { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            header.Pressed += () => OnRenamePressed(tile.Id);
            row.AddChild(header);
            columns.Add(header);

            Button collidesToggle = new Button {
                Text = tile.Collides ? "Collides: On" : "Collides: Off",
                ToggleMode = true,
                ButtonPressed = tile.Collides,
            };
            collidesToggle.Pressed += () => OnCollidesPressed(tile.Id, collidesToggle);
            row.AddChild(collidesToggle);
            columns.Add(collidesToggle);

            // DiVoid #7551 Phase 2: appending a frame is what turns a simple tile animated (structural —
            // design #7580 §7/§10), so this is offered on every tile, not gated behind an "animate" toggle.
            Button addFrame = new Button { Text = "+ Add Frame…" };
            addFrame.Pressed += () => OnAddFramePressed(tile.Id);
            row.AddChild(addFrame);
            columns.Add(addFrame);

            Button delete = new Button { Text = pendingDeleteId == tile.Id ? "Confirm Remove?" : "Remove" };
            delete.Pressed += () => OnRemovePressed(tile.Id);
            row.AddChild(delete);
            columns.Add(delete);

            return columns;
        }

        // DiVoid #7551 Phase 2: an animated tile's speed row — a display label plus two focusable buttons
        // that immediately step AnimationSpeedLadder and commit (no two-press edit-lock like the layer
        // panel's Scroll stepper needs: these buttons don't double-book left/right for spatial nav, they
        // are just ordinary grid stops, so a plain press-to-commit button is enough).
        List<Control> BuildAnimationSpeedRow(EditableTile tile) {
            HBoxContainer row = new HBoxContainer();
            listBox.AddChild(row);

            List<Control> columns = new List<Control>();

            Label indent = new Label { Text = "    " };
            row.AddChild(indent);

            Button slower = new Button { Text = "◄ Slower" };
            slower.Pressed += () => OnAnimationSpeedStepPressed(tile.Id, -1);
            row.AddChild(slower);
            columns.Add(slower);

            Label speed = new Label { Text = $"{tile.AnimationSpeed:0.#} fps" };
            speed.AddThemeColorOverride("font_color", EditorTheme.Text);
            row.AddChild(speed);

            Button faster = new Button { Text = "Faster ►" };
            faster.Pressed += () => OnAnimationSpeedStepPressed(tile.Id, +1);
            row.AddChild(faster);
            columns.Add(faster);

            return columns;
        }

        // DiVoid #7551 Phase 2: one row per animation frame beyond the tile's primary graphic (frame 0).
        // frameIndex is 0-based into EditableTile.Frames — overall animation frame frameIndex + 2.
        List<Control> BuildFrameRow(EditableTile tile, int frameIndex) {
            HBoxContainer row = new HBoxContainer();
            listBox.AddChild(row);

            List<Control> columns = new List<Control>();
            EditableTileFrame frame = tile.Frames[frameIndex];

            Label indent = new Label { Text = "    " };
            row.AddChild(indent);

            TextureRect thumb = new TextureRect {
                CustomMinimumSize = new Vector2(24f, 24f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Texture = LoadTexture(frame.Graphic),
            };
            row.AddChild(thumb);

            Label label = new Label { Text = $"Frame {frameIndex + 2}", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(label);

            bool pendingThisFrame = pendingRemoveFrameTileId == tile.Id && pendingRemoveFrameIndex == frameIndex;
            Button remove = new Button { Text = pendingThisFrame ? "Confirm Remove?" : "Remove Frame" };
            remove.Pressed += () => OnRemoveFramePressed(tile.Id, frameIndex);
            row.AddChild(remove);
            columns.Add(remove);

            return columns;
        }

        static ImageTexture LoadTexture(byte[] png) {
            Image image = new Image();
            if (image.LoadPngFromBuffer(png) != Error.Ok)
                return null;
            return ImageTexture.CreateFromImage(image);
        }

        void OnAddTilePressed() {
            pendingDeleteId = -1;
            pendingFrameTileId = -1;
            importDialog.PopupCentered();
        }

        /// <summary>
        /// Opens the same import dialog as "+ Add Tile", but the picked graphic becomes the tile's NEXT
        /// animation frame instead of a new tile (DiVoid #7551 Phase 2) — see <see cref="pendingFrameTileId"/>.
        /// </summary>
        void OnAddFramePressed(int tileId) {
            pendingDeleteId = -1;
            pendingRemoveFrameTileId = -1;
            pendingRemoveFrameIndex = -1;
            pendingFrameTileId = tileId;
            importDialog.PopupCentered();
        }

        /// <summary>
        /// Imports a graphic (scaled to <see cref="tileSize"/> per <see cref="TileGraphicImport"/>, same as
        /// "+ Add Tile") and routes it to either a new tile or the next animation frame of an existing tile,
        /// depending on which button opened the dialog (<see cref="pendingFrameTileId"/>).
        /// </summary>
        void OnGraphicFileSelected(string path) {
            if (session == null)
                return;

            byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
            if (bytes == null || bytes.Length == 0) {
                GD.PrintErr($"TileSetEditor: could not read '{path}'.");
                return;
            }

            Image probe = new Image();
            if (probe.LoadPngFromBuffer(bytes) != Error.Ok) {
                GD.PrintErr($"TileSetEditor: '{path}' is not a readable PNG.");
                return;
            }

            int sourceWidth = probe.GetWidth();
            int sourceHeight = probe.GetHeight();
            if (TileGraphicImport.NeedsResize(sourceWidth, sourceHeight, tileSize)) {
                probe.Resize(tileSize, tileSize, Image.Interpolation.Lanczos);
                bytes = probe.SavePngToBuffer();
                GD.Print($"TileSetEditor: '{path}' was {sourceWidth}x{sourceHeight}, scaled to {tileSize}x{tileSize} to fill the tile.");
            }

            if (pendingFrameTileId == -1) {
                int id = session.AddTile(bytes, collides: false);
                GD.Print($"TileSetEditor: imported tile #{id} from '{path}'.");
            } else if (session.AddFrame(pendingFrameTileId, bytes)) {
                GD.Print($"TileSetEditor: added an animation frame to tile #{pendingFrameTileId} from '{path}'.");
            } else {
                GD.PrintErr($"TileSetEditor: could not add a frame to tile #{pendingFrameTileId} (it no longer exists).");
            }

            TileSetModelChanged?.Invoke();
            Rebuild();
        }

        void OnAnimationSpeedStepPressed(int tileId, int direction) {
            pendingDeleteId = -1;
            EditableTile tile = Find(tileId);
            if (tile == null)
                return;

            double stepped = AnimationSpeedLadder.Step(tile.AnimationSpeed, direction);
            if (session.SetAnimationSpeed(tileId, stepped)) {
                GD.Print($"TileSetEditor: tile #{tileId} animation speed set to {stepped:0.#} fps.");
                TileSetModelChanged?.Invoke();
            }
            Rebuild();
        }

        // Confirm-gated (mirrors OnRemovePressed): frame removal is not undoable this increment. Removing
        // the tile's last extra frame is the animated→simple structural transition (design #7580 §7/§10).
        void OnRemoveFramePressed(int tileId, int frameIndex) {
            pendingDeleteId = -1;
            if (pendingRemoveFrameTileId != tileId || pendingRemoveFrameIndex != frameIndex) {
                pendingRemoveFrameTileId = tileId;
                pendingRemoveFrameIndex = frameIndex;
                Rebuild();
                return;
            }

            pendingRemoveFrameTileId = -1;
            pendingRemoveFrameIndex = -1;
            if (session.RemoveFrame(tileId, frameIndex)) {
                GD.Print($"TileSetEditor: removed animation frame {frameIndex + 2} from tile #{tileId}.");
                TileSetModelChanged?.Invoke();
            }
            Rebuild();
        }

        // Mirrors LayerManagerPanel.OnRenamePressed: opens the shared keyboard seeded with the tile's
        // current name; Done applies via TileSetEditSession.RenameTile, Cancel touches nothing.
        void OnRenamePressed(int id) {
            if (session == null || keyboard == null)
                return;

            pendingDeleteId = -1;
            pendingRemoveFrameTileId = -1;
            pendingRemoveFrameIndex = -1;
            EditableTile tile = Find(id);
            string currentName = tile?.Name ?? string.Empty;
            keyboard.RequestText($"Rename tile #{id}", currentName, newName => ApplyRename(id, newName));
        }

        void ApplyRename(int id, string newName) {
            if (session.RenameTile(id, newName)) {
                GD.Print($"TileSetEditor: renamed tile #{id} to '{newName}'.");
                TileSetModelChanged?.Invoke();
            }
            Rebuild();
        }

        void OnCollidesPressed(int id, Button toggle) {
            pendingDeleteId = -1;
            pendingRemoveFrameTileId = -1;
            pendingRemoveFrameIndex = -1;
            if (session.SetTileCollides(id, toggle.ButtonPressed)) {
                GD.Print($"TileSetEditor: tile #{id} collides set to {toggle.ButtonPressed}.");
                TileSetModelChanged?.Invoke();
            }
            Rebuild();
        }

        // Confirm-gated (mirrors LayerManagerPanel.OnDeletePressed): removal is not undoable this
        // increment, and a removed id is never reused (EditableTileSet), so an accidental remove would
        // both lose the tile and permanently retire its id.
        void OnRemovePressed(int id) {
            if (pendingDeleteId != id) {
                pendingDeleteId = id;
                Rebuild();
                return;
            }

            pendingDeleteId = -1;
            pendingRemoveFrameTileId = -1;
            pendingRemoveFrameIndex = -1;
            if (session.RemoveTile(id)) {
                GD.Print($"TileSetEditor: removed tile #{id}.");
                TileSetModelChanged?.Invoke();
            }
            Rebuild();
        }

        EditableTile Find(int id) {
            foreach (EditableTile tile in session.TileSet.Tiles)
                if (tile.Id == id)
                    return tile;
            return null;
        }

        public override void _GuiInput(InputEvent @event) {
            if (!Visible || (keyboard != null && keyboard.IsOpen) || (importDialog != null && importDialog.Visible))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                AcceptEvent();
                Close();
            }
        }

        // Belt-and-suspenders close path, exactly as LayerManagerPanel/PackageBrowser.
        public override void _UnhandledInput(InputEvent @event) {
            if (!Visible || @event.IsEcho() || (keyboard != null && keyboard.IsOpen) || (importDialog != null && importDialog.Visible))
                return;

            if (@event.IsActionPressed("ui_cancel")) {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        void Close() {
            Visible = false;
            pendingDeleteId = -1;
            pendingFrameTileId = -1;
            pendingRemoveFrameTileId = -1;
            pendingRemoveFrameIndex = -1;
            Closed?.Invoke();
        }
    }
}
