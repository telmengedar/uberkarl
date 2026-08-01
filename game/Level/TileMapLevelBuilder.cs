using System.Collections.Generic;
using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Translates a Godot-free <see cref="ResolvedLevel"/> into a tree of <see cref="TileMapLayer"/>s.
    /// Collidable tiles get a full-tile collision polygon on a TileSet physics layer, but only the
    /// <see cref="LayerRole.Main"/> layers use that physics-enabled TileSet — background and
    /// foreground layers use a plain TileSet with no physics layer, so they never collide.
    /// </summary>
    public static class TileMapLevelBuilder {

        public static Node2D Build(ResolvedLevel level) {
            BuiltTileSet solid = BuildTileSet(level, withCollision: true);
            BuiltTileSet plain = BuildTileSet(level, withCollision: false);

            Node2D root = new Node2D { Name = "Level" };
            foreach (ResolvedLayer layer in level.Layers) {
                BuiltTileSet chosen = layer.Role == LayerRole.Main ? solid : plain;
                TileMapLayer mapLayer = new TileMapLayer { Name = layer.Name, TileSet = chosen.Set };
                for (int y = 0; y < level.Height; y++) {
                    for (int x = 0; x < level.Width; x++) {
                        int id = layer.Cells[y * level.Width + x];
                        if (id == LayerDefinition.EmptyCell)
                            continue;
                        if (chosen.SourceByTile.TryGetValue(id, out int sourceId))
                            mapLayer.SetCell(new Vector2I(x, y), sourceId, Vector2I.Zero);
                    }
                }

                root.AddChild(mapLayer);
            }

            return root;
        }

        static BuiltTileSet BuildTileSet(ResolvedLevel level, bool withCollision) {
            TileSet tileSet = new TileSet { TileSize = new Vector2I(level.TileSize, level.TileSize) };
            if (withCollision)
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

                if (withCollision && level.CollidingTileIds.Contains(graphic.Key))
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
