using System;
using Godot;
using Uberkarl.Content;
using Uberkarl.Editor;
using Uberkarl.Editor.Input;

namespace Uberkarl {

    /// <summary>
    /// The authoring surface. A <see cref="Control"/> that renders the level being edited (built once
    /// via <see cref="TileMapLevelBuilder.BuildEditable"/>, then updated one cell at a time) and turns
    /// device input into grid-cell interactions it raises to the controller. It supports three input
    /// modes with parity: the <b>mouse</b> hovers and clicks a cell; a <b>grid cursor</b> — the pointer
    /// stand-in for gamepad and keyboard, which have none — is moved cell-by-cell with the cursor actions
    /// and acted on with paint/erase. The two stay coherent: a mouse click snaps the grid cursor to the
    /// clicked cell. It owns only the view, the pointer→cell mapping, and the grid cursor position; it
    /// never touches the edit model.
    /// </summary>
    public partial class EditorCanvas : Control {

        // Grid-cursor key/stick repeat: one initial delay, then a faster steady repeat while held.
        const float MoveInitialDelay = 0.32f;
        const float MoveRepeatRate = 0.06f;

        // The tile layers render inside this child; ShowBehindParent pushes the whole subtree behind the
        // Control's own _Draw so the grid and cursor overlay draw on top of the tiles.
        Node2D worldRoot;
        TileMapLevelBuilder.BuiltLevel built;

        int tileSize = 16;
        int width;
        int height;
        float viewScale = 1f;
        Vector2 viewOffset = Vector2.Zero;

        int hoverX = -1;
        int hoverY = -1;
        bool pointerDown;
        int lastCellX = int.MinValue;
        int lastCellY = int.MinValue;

        // The device-neutral grid cursor (gamepad / keyboard). Null until a level is set.
        GridCursor cursor;
        float moveCooldown;
        bool moveHeld;

        /// <summary>Set by the controller while a pop-in radial is open or a toolbar/panel focus-zone is
        /// active: directional input is being consumed by that surface, so the grid cursor must freeze even
        /// if focus momentarily lands back on the canvas. This is the robust suppression the "menu holds
        /// focus" assumption alone did not guarantee — Godot's directional focus navigation can bounce focus
        /// off the radial onto this full-rect canvas while a stick/D-pad aim is held.</summary>
        public bool DirectionalInputCaptured { get; set; }

        /// <summary>Raised when a cell is activated with the primary action — a mouse click/drag, or the
        /// paint action at the grid cursor. The controller applies the active tool to this cell.</summary>
        public event Action<int, int> CellPressed;

        /// <summary>Raised when the erase action is used at the grid cursor — an explicit erase regardless
        /// of the active tool (the device convenience that has no single-button mouse equivalent).</summary>
        public event Action<int, int> CellErased;

        public override void _Ready() {
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All; // focusable, so gamepad/keyboard can direct input here
            // The cursor-move actions share the arrow keys / D-pad / stick with Godot's ui_up/down/left/
            // right focus navigation. Pinning the four directional focus neighbours to self keeps focus on
            // the canvas while those move the grid cursor; leaving the canvas is done deliberately with the
            // focus-next action (Tab / B) instead of by nudging a direction.
            NodePath self = new NodePath(".");
            FocusNeighborLeft = self;
            FocusNeighborRight = self;
            FocusNeighborTop = self;
            FocusNeighborBottom = self;
            ClipContents = true;
            worldRoot = new Node2D { Name = "World", ShowBehindParent = true };
            AddChild(worldRoot);
            Resized += OnResized;
        }

        /// <summary>Builds (or rebuilds) the rendered level from a resolved snapshot of the edit model.</summary>
        public void SetLevel(ResolvedLevel level) {
            foreach (Node child in worldRoot.GetChildren())
                child.QueueFree();

            built = TileMapLevelBuilder.BuildEditable(level);
            worldRoot.AddChild(built.Root);
            tileSize = level.TileSize;
            width = level.Width;
            height = level.Height;

            if (cursor == null)
                cursor = new GridCursor(width, height);
            else
                cursor.Resize(width, height);

            Recenter();
            QueueRedraw();
        }

        /// <summary>Reflects a single committed cell change on the rendered layer (paint or erase).</summary>
        public void Apply(CellChange change) {
            if (built == null || change.LayerIndex < 0 || change.LayerIndex >= built.Layers.Count)
                return;

            TileMapLayer layer = built.Layers[change.LayerIndex];
            Vector2I cell = new Vector2I(change.X, change.Y);
            if (change.TileId == LayerDefinition.EmptyCell)
                layer.EraseCell(cell);
            else if (built.SourceByTile.TryGetValue(change.TileId, out int sourceId))
                layer.SetCell(cell, sourceId, Vector2I.Zero);
        }

        // Poll the held cursor-move actions only while this surface has focus AND no radial/panel is
        // capturing directional input (CursorInputGate), so the same D-pad / stick / arrow keys drive the
        // grid cursor here but steer an open wheel or navigate a focused panel instead — never both. A
        // repeat clock gives the familiar "step, pause, then stream" feel for a held direction.
        public override void _Process(double delta) {
            if (cursor == null || !CursorInputGate.AllowsCursorMovement(HasFocus(), DirectionalInputCaptured)) {
                moveHeld = false;
                return;
            }

            int dx = AxisFor(EditorAction.MoveCursorRight, EditorAction.MoveCursorLeft);
            int dy = AxisFor(EditorAction.MoveCursorDown, EditorAction.MoveCursorUp);
            if (dx == 0 && dy == 0) {
                moveHeld = false; // released — the next press steps immediately
                return;
            }

            if (!moveHeld) {
                // Fresh press: step once now, then hold for the initial delay before the repeat stream.
                moveHeld = true;
                StepCursor(dx, dy);
                moveCooldown = MoveInitialDelay;
                return;
            }

            moveCooldown -= (float)delta;
            if (moveCooldown <= 0f) {
                StepCursor(dx, dy);
                moveCooldown = MoveRepeatRate;
            }
        }

        void StepCursor(int dx, int dy) {
            if (cursor.TryMove(dx, dy))
                QueueRedraw();
        }

        static int AxisFor(EditorAction positive, EditorAction negative) {
            int value = 0;
            if (Input.IsActionPressed(EditorActionMap.NameOf(positive)))
                value += 1;
            if (Input.IsActionPressed(EditorActionMap.NameOf(negative)))
                value -= 1;
            return value;
        }

        public override void _GuiInput(InputEvent @event) {
            // Grid-cursor paint / erase (gamepad button, keyboard) — act at the cursor cell.
            if (@event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Paint))) {
                if (cursor != null)
                    CellPressed?.Invoke(cursor.X, cursor.Y);
                AcceptEvent();
                return;
            }
            if (@event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Erase))) {
                if (cursor != null)
                    CellErased?.Invoke(cursor.X, cursor.Y);
                AcceptEvent();
                return;
            }

            // Mouse: hover + click/drag paints via the active tool, and snaps the shared cursor to the cell.
            if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left) {
                if (button.Pressed) {
                    GrabFocus();
                    pointerDown = true;
                    lastCellX = int.MinValue;
                    lastCellY = int.MinValue;
                    if (TryCell(button.Position, out int cx, out int cy) && cursor != null) {
                        cursor.MoveTo(cx, cy);
                        QueueRedraw();
                    }
                    EmitCellAt(button.Position);
                } else {
                    pointerDown = false;
                }
                AcceptEvent();
            } else if (@event is InputEventMouseMotion motion) {
                UpdateHover(motion.Position);
                if (pointerDown)
                    EmitCellAt(motion.Position);
            }
        }

        /// <summary>The global (viewport-space) centre of the grid-cursor cell — where a pop-in menu opened
        /// by a gamepad/keyboard trigger should appear, so it frames the cell the action will land on.</summary>
        public Vector2 CursorGlobalCenter() {
            if (cursor == null)
                return GlobalPosition + Size / 2f;
            Vector2 local = viewOffset + (new Vector2(cursor.X, cursor.Y) + new Vector2(0.5f, 0.5f)) * (tileSize * viewScale);
            return GlobalPosition + local;
        }

        /// <summary>Erase the cell under a global (viewport-space) point, if it maps to a cell — the mouse
        /// right-click-tap erase convenience, resolved by the controller and routed like any other erase.</summary>
        public void EraseAtGlobal(Vector2 globalPosition) {
            Vector2 local = globalPosition - GlobalPosition;
            if (TryCell(local, out int cx, out int cy)) {
                if (cursor != null && cursor.MoveTo(cx, cy))
                    QueueRedraw();
                CellErased?.Invoke(cx, cy);
            }
        }

        void EmitCellAt(Vector2 localPosition) {
            if (!TryCell(localPosition, out int cx, out int cy))
                return;
            if (cx == lastCellX && cy == lastCellY)
                return; // still in the same cell during a drag — do not re-fire
            lastCellX = cx;
            lastCellY = cy;
            CellPressed?.Invoke(cx, cy);
        }

        void UpdateHover(Vector2 localPosition) {
            int previousX = hoverX;
            int previousY = hoverY;
            if (TryCell(localPosition, out int cx, out int cy)) {
                hoverX = cx;
                hoverY = cy;
            } else {
                hoverX = -1;
                hoverY = -1;
            }

            if (hoverX != previousX || hoverY != previousY)
                QueueRedraw();
        }

        bool TryCell(Vector2 localPosition, out int cx, out int cy) {
            cx = cy = -1;
            if (viewScale <= 0f)
                return false;
            Vector2 world = (localPosition - viewOffset) / viewScale;
            int x = Mathf.FloorToInt(world.X / tileSize);
            int y = Mathf.FloorToInt(world.Y / tileSize);
            if (x < 0 || y < 0 || x >= width || y >= height)
                return false;
            cx = x;
            cy = y;
            return true;
        }

        void OnResized() {
            Recenter();
            QueueRedraw();
        }

        // Fit the whole level into the panel with a small margin and centre it.
        void Recenter() {
            if (width <= 0 || height <= 0)
                return;

            Vector2 levelPixels = new Vector2(width * tileSize, height * tileSize);
            Vector2 panel = Size;
            if (panel.X <= 0 || panel.Y <= 0)
                return;

            viewScale = Mathf.Min(panel.X / levelPixels.X, panel.Y / levelPixels.Y) * 0.95f;
            viewOffset = (panel - levelPixels * viewScale) / 2f;
            worldRoot.Position = viewOffset;
            worldRoot.Scale = new Vector2(viewScale, viewScale);
        }

        public override void _Draw() {
            if (width <= 0 || height <= 0)
                return;

            Vector2 origin = viewOffset;
            float step = tileSize * viewScale;
            Vector2 size = new Vector2(width, height) * step;

            // The tile layers render behind this Control (worldRoot.ShowBehindParent), so everything drawn
            // here is a true overlay on top of the tiles. Grid lines are kept translucent so tiles read
            // through them; no opaque backing is drawn or it would hide the tiles.
            Color gridColor = new Color(1f, 1f, 1f, 0.10f);
            for (int x = 0; x <= width; x++) {
                float px = origin.X + x * step;
                DrawLine(new Vector2(px, origin.Y), new Vector2(px, origin.Y + size.Y), gridColor);
            }
            for (int y = 0; y <= height; y++) {
                float py = origin.Y + y * step;
                DrawLine(new Vector2(origin.X, py), new Vector2(origin.X + size.X, py), gridColor);
            }

            // Level border.
            DrawRect(new Rect2(origin, size), new Color(0.4f, 0.45f, 0.55f), false, 1.5f);

            // Hovered-cell highlight (mouse) — a soft amber wash.
            if (hoverX >= 0 && hoverY >= 0) {
                Vector2 cellPos = origin + new Vector2(hoverX, hoverY) * step;
                DrawRect(new Rect2(cellPos, new Vector2(step, step)), new Color(1f, 0.85f, 0.2f, 0.25f));
            }

            // Grid cursor (gamepad / keyboard) — a bold outline so it reads as "where an action lands".
            // Bright while this surface is focused; dimmed when focus is on a panel, so its position is
            // still visible but clearly not the active input target.
            if (cursor != null) {
                Vector2 cellPos = origin + new Vector2(cursor.X, cursor.Y) * step;
                bool active = HasFocus();
                float alpha = active ? 1f : 0.45f;
                float thickness = active ? 2.5f : 1.5f;
                DrawRect(new Rect2(cellPos, new Vector2(step, step)), new Color(1f, 0.85f, 0.2f, active ? 0.18f : 0.08f));
                DrawRect(new Rect2(cellPos, new Vector2(step, step)), new Color(1f, 0.85f, 0.2f, alpha), false, thickness);
            }
        }
    }
}
