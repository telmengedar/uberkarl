using System;
using Godot;
using Uberkarl.Content;
using Uberkarl.Editor;

namespace Uberkarl {

    /// <summary>
    /// The authoring surface. A <see cref="Control"/> that renders the level being edited (built once
    /// via <see cref="TileMapLevelBuilder.BuildEditable"/>, then updated one cell at a time) and turns
    /// mouse clicks and drags into grid-cell interactions it raises to the controller. It owns only the
    /// view and the pointer→cell mapping; it never touches the edit model. The level is fit to the panel
    /// (scaled and centred); a grid and a hovered-cell highlight are drawn on top.
    /// </summary>
    public partial class EditorCanvas : Control {

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

        /// <summary>Raised when the pointer presses or drags onto a grid cell (x, y in tile units).</summary>
        public event Action<int, int> CellPressed;

        public override void _Ready() {
            MouseFilter = MouseFilterEnum.Stop;
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

        public override void _GuiInput(InputEvent @event) {
            if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left) {
                if (button.Pressed) {
                    pointerDown = true;
                    lastCellX = int.MinValue;
                    lastCellY = int.MinValue;
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

            // Hovered-cell highlight.
            if (hoverX >= 0 && hoverY >= 0) {
                Vector2 cellPos = origin + new Vector2(hoverX, hoverY) * step;
                DrawRect(new Rect2(cellPos, new Vector2(step, step)), new Color(1f, 0.85f, 0.2f, 0.25f));
                DrawRect(new Rect2(cellPos, new Vector2(step, step)), new Color(1f, 0.85f, 0.2f, 0.9f), false, 1.5f);
            }
        }
    }
}
