using System;
using System.Collections.Generic;
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
    /// and acted on with paint/erase. The two stay coherent: the grid cursor follows the pointer as it
    /// hovers or clicks. It owns only the view, the pointer→cell mapping, and the grid cursor position; it
    /// never touches the edit model.
    /// </summary>
    public partial class EditorCanvas : Control {

        // Grid-cursor key/stick repeat: one initial delay, then a faster steady repeat while held.
        const float MoveInitialDelay = 0.32f;
        const float MoveRepeatRate = 0.06f;

        // Fixed zoom levels for the editor viewport (DiVoid #7576): the editor no longer auto-fits the
        // whole level on screen (unusable at realistic sizes — a 100-wide level would shrink to
        // illegible), it renders at one of these fixed tile scales and the view scrolls to follow the grid
        // cursor instead, clamped to the level bounds. The default (index 3 = 3x) matches
        // PlayRuntimeBuilder.CameraZoom — "comparable to the play zoom" per Toni's ask — so authoring and
        // playtesting read at a similar scale.
        static readonly float[] ZoomLevels = { 1f, 1.5f, 2f, 3f, 4f, 6f };
        const int DefaultZoomIndex = 3;

        // The tile layers render inside this child; ShowBehindParent pushes the whole subtree behind the
        // Control's own _Draw so the grid and cursor overlay draw on top of the tiles.
        Node2D worldRoot;
        TileMapLevelBuilder.BuiltLevel built;

        int tileSize = 16;
        int width;
        int height;
        int zoomIndex = DefaultZoomIndex;
        float viewScale = ZoomLevels[DefaultZoomIndex];
        Vector2 viewOffset = Vector2.Zero;

        int hoverX = -1;
        int hoverY = -1;
        Vector2 lastPointerLocal;
        bool pointerDown;
        int lastCellX = int.MinValue;
        int lastCellY = int.MinValue;

        // The device-neutral grid cursor (gamepad / keyboard). Null until a level is set.
        GridCursor cursor;
        float moveCooldown;
        bool moveHeld;

        IReadOnlyList<EditableObjectPlacement> overlayObjects = Array.Empty<EditableObjectPlacement>();
        IReadOnlyList<AreaTriggerDefinition> overlayTriggers = Array.Empty<AreaTriggerDefinition>();
        IReadOnlyList<TileBehaviorOverride> overlayTileBehaviorOverrides = Array.Empty<TileBehaviorOverride>();

        /// <summary>Set by the controller while a pop-in radial is open or a toolbar/panel focus-zone is
        /// active: directional input is being consumed by that surface, so the grid cursor must freeze even
        /// if focus momentarily lands back on the canvas. This is the robust suppression the "menu holds
        /// focus" assumption alone did not guarantee — Godot's directional focus navigation can bounce focus
        /// off the radial onto this full-rect canvas while a stick/D-pad aim is held.</summary>
        public bool DirectionalInputCaptured { get; set; }

        /// <summary>True when the pointer was the last input to position the grid cursor.</summary>
        public bool PointerDrivesCursor { get; private set; }

        /// <summary>The pointer's own global (viewport-space) position, from the most recent motion event
        /// this surface received.</summary>
        public Vector2 PointerGlobalPosition => GlobalPosition + lastPointerLocal;

        /// <summary>The grid cursor's current cell.</summary>
        public (int X, int Y) CursorCell => cursor is null ? (-1, -1) : (cursor.X, cursor.Y);

        /// <summary>Set once by the controller in <c>BuildUi</c> to <c>AnyModalOpen</c> — a live predicate,
        /// not a per-frame snapshot, so a modal opened or closed mid-frame is still seen correctly. Checked
        /// at every site that mutates the level document or drives a mouse action: <see cref="EmitCellAt"/>,
        /// <see cref="EraseAtGlobal"/>, and the mouse handling in <see cref="_GuiInput"/>.</summary>
        public Func<bool> MutationLocked { get; set; } = () => false;

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
            // right focus navigation, and Tab/B share the editor focus-next action with Godot's
            // ui_focus_next. Pinning ALL six focus neighbours (four directional plus next/previous) to self
            // keeps focus on the canvas while those move the grid cursor; leaving the canvas is done
            // deliberately with the editor focus-next action (which drives the zone cycle), never as a side
            // effect of a directional nudge or a Tab. Without the next/previous pins, a keyboard Tab's
            // ui_focus_next would race the zone cycle and could strand focus on an arbitrary control.
            NodePath self = new NodePath(".");
            FocusNeighborLeft = self;
            FocusNeighborRight = self;
            FocusNeighborTop = self;
            FocusNeighborBottom = self;
            FocusNext = self;
            FocusPrevious = self;
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

            UpdateView();
            QueueRedraw();
        }

        /// <summary>Sets the placed-object/trigger/tile-override data the authoring overlay draws. <c>null</c>
        /// lists are treated as empty; <paramref name="tileBehaviorOverrides"/> is expected pre-filtered to
        /// the active layer.</summary>
        public void SetOverlay(IReadOnlyList<EditableObjectPlacement> objects, IReadOnlyList<AreaTriggerDefinition> triggers, IReadOnlyList<TileBehaviorOverride> tileBehaviorOverrides) {
            overlayObjects = objects ?? Array.Empty<EditableObjectPlacement>();
            overlayTriggers = triggers ?? Array.Empty<AreaTriggerDefinition>();
            overlayTileBehaviorOverrides = tileBehaviorOverrides ?? Array.Empty<TileBehaviorOverride>();
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

        /// <summary>
        /// Terrain id → Godot (terrainSet, terrain) index lookup for the currently-built level (DiVoid #7551
        /// Phase 3) — what the controller's terrain-reflow pass (<c>LevelEditor.ReflowTerrain</c>) needs to
        /// re-issue Godot's own terrain-connect resolution after a paint/erase, so a neighbour edit re-flows
        /// the border live, exactly like the runtime (design #7580 §9 — "editor preview must resolve terrains
        /// with the SAME mechanism the runtime uses").
        /// </summary>
        public IReadOnlyDictionary<int, TileSetBuilder.TerrainIndex> TerrainIndexByTerrainId => built?.TerrainIndexByTerrainId;

        /// <summary>
        /// Re-resolves terrain <paramref name="terrainSetIndex"/>/<paramref name="terrainIndex"/> over
        /// <paramref name="cells"/> on the given layer via Godot's own <c>TileMapLayer.SetCellsTerrainConnect</c>
        /// — the SAME call <see cref="TileMapLevelBuilder.ConnectTerrain"/> uses at load/build time, so the
        /// live canvas preview never diverges from what a fresh load (or playtest) would render.
        /// </summary>
        public void ReconnectTerrain(int layerIndex, int terrainSetIndex, int terrainIndex, Godot.Collections.Array<Vector2I> cells) {
            if (built == null || layerIndex < 0 || layerIndex >= built.Layers.Count || cells.Count == 0)
                return;
            built.Layers[layerIndex].SetCellsTerrainConnect(cells, terrainSetIndex, terrainIndex, ignoreEmptyTerrains: true);
        }

        /// <summary>
        /// Fills, on layer <paramref name="layerIndex"/>, every cell in <paramref name="cells"/> that the
        /// preceding <see cref="ReconnectTerrain"/> call just left with no tile (DiVoid #7638) with the tile
        /// <paramref name="defaultTileId"/> names — empirically (Toni, 2026-08-04 live test), Godot's
        /// <c>SetCellsTerrainConnect</c> leaves a cell whose real neighbour pattern matches no declared
        /// variant completely empty rather than picking a "closest" one; this is what
        /// <c>LevelEditor.ReflowTerrain</c> calls right after <see cref="ReconnectTerrain"/> so the LIVE
        /// terrain brush shows the exact same deterministic fallback the runtime (<c>TileMapLevelBuilder</c>)
        /// applies, per design #7580 §9 ("editor preview must resolve terrains with the SAME mechanism the
        /// runtime uses"). A cell Godot DID resolve to a real variant is left untouched. No-op (not a crash)
        /// if <paramref name="defaultTileId"/> has no source in the current tile set — a stale
        /// authoring-time reference is defensive, not fatal, here.
        /// </summary>
        public void ApplyDefaultTile(int layerIndex, int defaultTileId, Godot.Collections.Array<Vector2I> cells) {
            if (built == null || layerIndex < 0 || layerIndex >= built.Layers.Count || cells.Count == 0)
                return;
            if (!built.SourceByTile.TryGetValue(defaultTileId, out int sourceId))
                return;

            TileMapLayer layer = built.Layers[layerIndex];
            foreach (Vector2I cell in cells) {
                if (layer.GetCellSourceId(cell) == -1)
                    layer.SetCell(cell, sourceId, Vector2I.Zero);
            }
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
            PointerDrivesCursor = false;
            if (cursor.TryMove(dx, dy)) {
                UpdateView(); // scroll to keep the cursor's new cell in view, clamped to the level bounds
                QueueRedraw();
            }
        }

        bool MoveCursorToCell(int cx, int cy, bool recenterView) {
            if (cursor == null)
                return false;
            PointerDrivesCursor = true;
            bool moved = cursor.MoveTo(cx, cy);
            if (moved && recenterView)
                UpdateView();
            return moved;
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
            // Grid-cursor paint / erase (gamepad button, keyboard) — act at the cursor cell, but ONLY while the
            // canvas truly owns input. When a radial is open or a non-canvas focus-zone is active, the same
            // confirm button (gamepad A = editor_paint) belongs to that surface: it must activate the focused
            // classic Control via ui_accept, not paint the cell underneath. So editor_paint/erase stays inert
            // here — we do NOT AcceptEvent, letting the confirm reach the focused Button/ItemList. Mirrors the
            // cursor-movement gate; both read the controller-set DirectionalInputCaptured flag.
            bool canvasOwnsInput = CursorInputGate.AllowsPrimaryAction(HasFocus(), DirectionalInputCaptured);
            if (canvasOwnsInput && !MutationLocked() && @event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Paint))) {
                if (cursor != null)
                    CellPressed?.Invoke(cursor.X, cursor.Y);
                AcceptEvent();
                return;
            }
            if (canvasOwnsInput && !MutationLocked() && @event.IsActionPressed(EditorActionMap.NameOf(EditorAction.Erase))) {
                if (cursor != null)
                    CellErased?.Invoke(cursor.X, cursor.Y);
                AcceptEvent();
                return;
            }

            // Mouse: hover + click/drag paints via the active tool, and snaps the shared cursor to the cell.
            if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left) {
                if (button.Pressed) {
                    if (MutationLocked())
                        return;
                    GrabFocus();
                    pointerDown = true;
                    lastCellX = int.MinValue;
                    lastCellY = int.MinValue;
                    if (TryCell(button.Position, out int cx, out int cy)) {
                        MoveCursorToCell(cx, cy, recenterView: true);
                        QueueRedraw();
                    }
                    EmitCellAt(button.Position);
                } else {
                    pointerDown = false;
                    lastCellX = int.MinValue;
                    lastCellY = int.MinValue;
                }
                AcceptEvent();
                return;
            }

            if (MutationLocked())
                return;

            if (@event is InputEventMouseButton wheel && wheel.Pressed &&
                (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown)) {
                // Mouse-wheel zoom is a direct convenience on the canvas (like the click/hover handling
                // above), independent of the editor_zoom_in/out actions LevelEditor dispatches for
                // keyboard/gamepad — the wheel only ever means "zoom" while hovering this surface.
                if (wheel.ButtonIndex == MouseButton.WheelUp)
                    ZoomIn();
                else
                    ZoomOut();
                AcceptEvent();
            } else if (@event is InputEventMouseMotion motion) {
                UpdateHover(motion.Position);
                if (pointerDown)
                    EmitCellAt(motion.Position);
            }
        }

        /// <summary>Steps the fixed viewport zoom in one level (never past the last preset). Re-scrolls the
        /// view so the grid cursor stays the scroll target at the new scale.</summary>
        public void ZoomIn() => SetZoomIndex(zoomIndex + 1);

        /// <summary>Steps the fixed viewport zoom out one level (never past the first preset).</summary>
        public void ZoomOut() => SetZoomIndex(zoomIndex - 1);

        void SetZoomIndex(int index) {
            int clamped = Math.Clamp(index, 0, ZoomLevels.Length - 1);
            if (clamped == zoomIndex)
                return;
            zoomIndex = clamped;
            UpdateView();
            QueueRedraw();
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
            if (MutationLocked())
                return;

            Vector2 local = globalPosition - GlobalPosition;
            if (TryCell(local, out int cx, out int cy)) {
                lastPointerLocal = local;
                if (MoveCursorToCell(cx, cy, recenterView: true))
                    QueueRedraw();
                CellErased?.Invoke(cx, cy);
            }
        }

        void EmitCellAt(Vector2 localPosition) {
            if (MutationLocked())
                return;
            if (!TryCell(localPosition, out int cx, out int cy))
                return;
            if (cx == lastCellX && cy == lastCellY)
                return; // still in the same cell during a drag — do not re-fire
            lastCellX = cx;
            lastCellY = cy;
            CellPressed?.Invoke(cx, cy);
        }

        void UpdateHover(Vector2 localPosition) {
            lastPointerLocal = localPosition;
            int previousX = hoverX;
            int previousY = hoverY;
            if (TryCell(localPosition, out int cx, out int cy)) {
                hoverX = cx;
                hoverY = cy;
                MoveCursorToCell(cx, cy, recenterView: false);
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
            UpdateView();
            QueueRedraw();
        }

        // Fixed zoom + cursor-follow, clamped to the level bounds (DiVoid #7576) — replaces the old
        // fit-the-whole-level-on-screen behaviour, which zoomed out to illegibility as a level grew (and
        // absurdly far in for a tiny one). The view now renders at one of the ZoomLevels presets and
        // scrolls to keep the grid cursor's cell in frame, clamped so it never shows past the level edges —
        // EditorViewportClamp reimplements the exact "LimitLeft/Top=0, LimitRight/Bottom=size" rule
        // PlayRuntimeBuilder.AttachCamera gets for free from Camera2D.Limit*, since this Control has no
        // Camera2D of its own to reuse directly (see the class doc comment).
        void UpdateView() {
            if (width <= 0 || height <= 0)
                return;

            Vector2 panel = Size;
            if (panel.X <= 0 || panel.Y <= 0)
                return;

            viewScale = ZoomLevels[zoomIndex];
            Vector2 levelPixels = new Vector2(width, height) * tileSize;

            // Scroll target: the grid cursor's cell centre, falling back to the level's centre before a
            // cursor exists (SetLevel always creates one before calling this, so this is only a defensive
            // fallback).
            Vector2 targetCenter = cursor != null
                ? (new Vector2(cursor.X, cursor.Y) + new Vector2(0.5f, 0.5f)) * tileSize
                : levelPixels / 2f;

            viewOffset = new Vector2(
                EditorViewportClamp.Offset(targetCenter.X, panel.X, levelPixels.X, viewScale),
                EditorViewportClamp.Offset(targetCenter.Y, panel.Y, levelPixels.Y, viewScale));
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

            DrawObjectOverlay(origin, step);
            DrawTriggerOverlay(origin, step);
            DrawTileBehaviorOverrideOverlay(origin, step);

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

        void DrawObjectOverlay(Vector2 origin, float step) {
            if (overlayObjects.Count == 0)
                return;

            Color fill = new Color(1f, 0.55f, 0.15f, 0.35f);
            Color outline = new Color(1f, 0.55f, 0.15f, 0.9f);
            Font font = GetThemeDefaultFont();
            int fontSize = GetThemeDefaultFontSize();

            foreach (EditableObjectPlacement placement in overlayObjects) {
                Vector2 cellPos = origin + new Vector2(placement.Placement.Cell.X, placement.Placement.Cell.Y) * step;
                Rect2 rect = new Rect2(cellPos, new Vector2(step, step));
                DrawRect(rect, fill);
                DrawRect(rect, outline, false, 2f);
                if (!string.IsNullOrEmpty(placement.Placement.Name))
                    DrawString(font, cellPos + new Vector2(2f, step - 4f), placement.Placement.Name,
                        HorizontalAlignment.Left, step - 4f, fontSize - 3, outline);
                if (placement.EffectiveBehavior is not null)
                    DrawBehaviorMarker(rect, outline);
            }
        }

        void DrawTriggerOverlay(Vector2 origin, float step) {
            if (overlayTriggers.Count == 0)
                return;

            Color outline = new Color(0.25f, 0.85f, 0.95f, 0.9f);
            Font font = GetThemeDefaultFont();
            int fontSize = GetThemeDefaultFontSize();

            foreach (AreaTriggerDefinition trigger in overlayTriggers) {
                Vector2 rectPos = origin + new Vector2(trigger.X, trigger.Y) * step;
                Vector2 rectSize = new Vector2(trigger.Width, trigger.Height) * step;
                Rect2 rect = new Rect2(rectPos, rectSize);
                DrawRect(rect, outline, false, 2f);
                if (!string.IsNullOrEmpty(trigger.Name))
                    DrawString(font, rectPos + new Vector2(2f, 14f), trigger.Name,
                        HorizontalAlignment.Left, rectSize.X - 4f, fontSize - 3, outline);
                DrawBehaviorMarker(rect, outline);
            }
        }

        void DrawTileBehaviorOverrideOverlay(Vector2 origin, float step) {
            if (overlayTileBehaviorOverrides.Count == 0)
                return;

            Color outline = new Color(0.8f, 0.45f, 0.95f, 0.9f);

            foreach (TileBehaviorOverride entry in overlayTileBehaviorOverrides) {
                Vector2 cellPos = origin + new Vector2(entry.Cell.X, entry.Cell.Y) * step;
                Rect2 rect = new Rect2(cellPos, new Vector2(step, step));
                DrawRect(rect, outline, false, 2f);
                DrawBehaviorMarker(rect, outline);
            }
        }

        const float BehaviorMarkerSize = 12f;

        void DrawBehaviorMarker(Rect2 rect, Color color) {
            float size = Mathf.Min(BehaviorMarkerSize, Mathf.Min(rect.Size.X, rect.Size.Y) * 0.6f);
            Vector2 topRight = rect.Position + new Vector2(rect.Size.X, 0f);
            DrawColoredPolygon(new[] { topRight + new Vector2(-size, 0f), topRight, topRight + new Vector2(0f, size) }, color);
        }
    }
}
