using System.Collections.Generic;
using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Translates a Godot-free <see cref="ResolvedLevel"/> into a tree of <see cref="TileMapLayer"/>s.
    /// All layers share ONE <see cref="TileSet"/> (a physics layer with a full-tile collision polygon
    /// on each colliding tile); each <see cref="TileMapLayer"/> sets <c>CollisionEnabled</c> from its
    /// layer's collision flag, so a non-collision layer never blocks the player even when it places a
    /// solid tile. Draw order is the layer array order (back to front), independent of collision.
    /// A layer whose <c>ScrollSpeed != 1.0</c> or that opts into <c>Repeat</c> is wrapped in a
    /// <see cref="Parallax2D"/> so it scrolls at that factor relative to the camera on the X axis only
    /// (Y stays world-locked — see <see cref="ScrollScaleFor"/>) and, when repeating, tiles across the
    /// scroll extent (repeat period = the layer's content size); finite world-locked layers are added
    /// directly and move with the camera naturally.
    /// </summary>
    public static class TileMapLevelBuilder {

        public static Node2D Build(ResolvedLevel level) {
            BuiltTileSet shared = BuildTileSet(level);

            // The layer's content size in pixels — used as the repeat period for a repeating layer so
            // its content tiles seamlessly across the scroll extent.
            Vector2 contentSize = new Vector2(level.Width * level.TileSize, level.Height * level.TileSize);

            Node2D root = new Node2D { Name = "Level" };
            foreach (ResolvedLayer layer in level.Layers) {
                TileMapLayer mapLayer = new TileMapLayer {
                    Name = layer.Name,
                    TileSet = shared.Set,
                    CollisionEnabled = layer.Collision,
                };
                FillLayer(mapLayer, layer, level, shared.SourceByTile);
                root.AddChild(WrapForScroll(mapLayer, layer, contentSize));
            }

            return root;
        }

        /// <summary>
        /// Builds the level for the editor canvas: the same shared tile set and per-layer grids, but the
        /// layers are added flat (no <see cref="Parallax2D"/> wrapping, collision off) so a cell maps 1:1
        /// to a screen position — the natural authoring view. The result exposes each layer node and the
        /// tile-id → atlas-source map so the editor can paint or erase a single cell in place
        /// (<c>SetCell</c>/<c>EraseCell</c>) without rebuilding the tree.
        /// </summary>
        public static BuiltLevel BuildEditable(ResolvedLevel level) {
            BuiltTileSet shared = BuildTileSet(level);

            Node2D root = new Node2D { Name = "Level" };
            List<TileMapLayer> layers = new List<TileMapLayer>(level.Layers.Count);
            foreach (ResolvedLayer layer in level.Layers) {
                TileMapLayer mapLayer = new TileMapLayer {
                    Name = layer.Name,
                    TileSet = shared.Set,
                    CollisionEnabled = false,
                };
                FillLayer(mapLayer, layer, level, shared.SourceByTile);
                root.AddChild(mapLayer);
                layers.Add(mapLayer);
            }

            return new BuiltLevel(root, layers, shared.SourceByTile);
        }

        static void FillLayer(TileMapLayer mapLayer, ResolvedLayer layer, ResolvedLevel level, Dictionary<int, int> sourceByTile) {
            for (int y = 0; y < level.Height; y++) {
                for (int x = 0; x < level.Width; x++) {
                    int id = layer.Cells[y * level.Width + x];
                    if (id == LayerDefinition.EmptyCell)
                        continue;
                    if (sourceByTile.TryGetValue(id, out int sourceId))
                        mapLayer.SetCell(new Vector2I(x, y), sourceId, Vector2I.Zero);
                }
            }
        }

        // A layer that is neither parallax nor repeating (scrollSpeed 1.0, repeat off) is added as-is
        // — it moves with the camera naturally. A layer that parallax-scrolls (scrollSpeed != 1.0) or
        // repeats is wrapped in a Parallax2D: scroll_scale = scrollSpeed relative to the camera, and
        // repeat_size = the layer's content size when repeating (so it tiles across the scroll extent)
        // or Vector2.Zero when finite (no tiling).
        static Node2D WrapForScroll(TileMapLayer mapLayer, ResolvedLayer layer, Vector2 contentSize) {
            if (layer.ScrollSpeed == 1f && !layer.Repeat)
                return mapLayer;

            Parallax2D parallax = new Parallax2D {
                Name = layer.Name,
                ScrollScale = ScrollScaleFor(layer.ScrollSpeed),
                RepeatSize = layer.Repeat ? contentSize : Vector2.Zero,
            };
            mapLayer.Name = layer.Name + "Tiles";
            parallax.AddChild(mapLayer);
            return parallax;
        }

        /// <summary>
        /// Maps a layer's <c>ScrollSpeed</c> to a <see cref="Parallax2D.ScrollScale"/>: X scrolls at the
        /// layer's speed, Y is always world-locked (1.0) — this is a side-scroller, so only horizontal
        /// parallax is wanted (DiVoid #7528).
        /// </summary>
        public static Vector2 ScrollScaleFor(float scrollSpeed) => new Vector2(scrollSpeed, 1f);

        static BuiltTileSet BuildTileSet(ResolvedLevel level) {
            TileSet tileSet = new TileSet { TileSize = new Vector2I(level.TileSize, level.TileSize) };
            tileSet.AddPhysicsLayer();

            Dictionary<int, int> sourceByTile = new Dictionary<int, int>();
            foreach (KeyValuePair<int, byte[]> graphic in level.TileGraphics) {
                Image image = new Image();
                Error status = image.LoadPngFromBuffer(graphic.Value);
                if (status != Error.Ok)
                    throw new LevelContentException($"Tile {graphic.Key} graphic is not a readable PNG (Godot error {status}).");

                ImageTexture texture = ImageTexture.CreateFromImage(image);
                TileSetAtlasSource source = new TileSetAtlasSource {
                    Texture = texture,
                    TextureRegionSize = new Vector2I(level.TileSize, level.TileSize),
                };
                source.CreateTile(Vector2I.Zero);
                sourceByTile[graphic.Key] = tileSet.AddSource(source);

                if (level.CollidingTileIds.Contains(graphic.Key))
                    AddFullTileCollision(source, level.TileSize);
            }

            return new BuiltTileSet(tileSet, sourceByTile);
        }

        static void AddFullTileCollision(TileSetAtlasSource source, int tileSize) {
            TileData data = source.GetTileData(Vector2I.Zero, 0);
            float half = tileSize / 2f;
            Vector2[] square = {
                new Vector2(-half, -half),
                new Vector2(half, -half),
                new Vector2(half, half),
                new Vector2(-half, half),
            };
            data.AddCollisionPolygon(0);
            data.SetCollisionPolygonPoints(0, 0, square);
        }

        /// <summary>
        /// The result of building a level for the editor: the parent node, the per-layer tile-map nodes
        /// (index-aligned to the level's layers) and the tile-id → atlas-source-id map used to place a
        /// tile on a layer. The editor paints a cell with <c>Layers[i].SetCell(cell, SourceByTile[id], Zero)</c>
        /// and erases with <c>Layers[i].EraseCell(cell)</c>.
        /// </summary>
        public sealed class BuiltLevel {
            public BuiltLevel(Node2D root, IReadOnlyList<TileMapLayer> layers, IReadOnlyDictionary<int, int> sourceByTile) {
                Root = root;
                Layers = layers;
                SourceByTile = sourceByTile;
            }

            public Node2D Root { get; }

            public IReadOnlyList<TileMapLayer> Layers { get; }

            public IReadOnlyDictionary<int, int> SourceByTile { get; }
        }

        readonly struct BuiltTileSet {
            public BuiltTileSet(TileSet set, Dictionary<int, int> sourceByTile) {
                Set = set;
                SourceByTile = sourceByTile;
            }

            public TileSet Set { get; }

            public Dictionary<int, int> SourceByTile { get; }
        }
    }
}
