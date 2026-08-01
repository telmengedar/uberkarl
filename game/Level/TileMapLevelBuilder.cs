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
    /// A layer whose <c>ScrollSpeed != 1.0</c> is wrapped in a <see cref="Parallax2D"/> so it scrolls
    /// at that factor relative to the camera; world-locked (1.0) layers are added directly and move
    /// with the camera naturally.
    /// </summary>
    public static class TileMapLevelBuilder {

        public static Node2D Build(ResolvedLevel level) {
            BuiltTileSet shared = BuildTileSet(level);

            Node2D root = new Node2D { Name = "Level" };
            foreach (ResolvedLayer layer in level.Layers) {
                TileMapLayer mapLayer = new TileMapLayer {
                    Name = layer.Name,
                    TileSet = shared.Set,
                    CollisionEnabled = layer.Collision,
                };
                for (int y = 0; y < level.Height; y++) {
                    for (int x = 0; x < level.Width; x++) {
                        int id = layer.Cells[y * level.Width + x];
                        if (id == LayerDefinition.EmptyCell)
                            continue;
                        if (shared.SourceByTile.TryGetValue(id, out int sourceId))
                            mapLayer.SetCell(new Vector2I(x, y), sourceId, Vector2I.Zero);
                    }
                }

                root.AddChild(WrapForScroll(mapLayer, layer));
            }

            return root;
        }

        // A world-locked layer (scrollSpeed 1.0) is added as-is. A parallax layer is wrapped in a
        // Parallax2D that scrolls its child at scroll_scale = scrollSpeed relative to the camera.
        // repeat_size = 0 keeps the layer finite (no tiling) — these are finite levels.
        static Node2D WrapForScroll(TileMapLayer mapLayer, ResolvedLayer layer) {
            if (layer.ScrollSpeed == 1f)
                return mapLayer;

            Parallax2D parallax = new Parallax2D {
                Name = layer.Name,
                ScrollScale = new Vector2(layer.ScrollSpeed, layer.ScrollSpeed),
                RepeatSize = Vector2.Zero,
            };
            mapLayer.Name = layer.Name + "Tiles";
            parallax.AddChild(mapLayer);
            return parallax;
        }

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
