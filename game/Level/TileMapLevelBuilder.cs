using System.Collections.Generic;
using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    public static class TileMapLevelBuilder {

        public static Node2D Build(ResolvedLevel level) {
            TileSet tileSet = new TileSet { TileSize = new Vector2I(level.TileSize, level.TileSize) };
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
            }

            Node2D root = new Node2D { Name = "Level" };
            foreach (ResolvedLayer layer in level.Layers) {
                TileMapLayer mapLayer = new TileMapLayer { Name = layer.Name, TileSet = tileSet };
                for (int y = 0; y < level.Height; y++) {
                    for (int x = 0; x < level.Width; x++) {
                        int id = layer.Cells[y * level.Width + x];
                        if (id == LayerDefinition.EmptyCell)
                            continue;
                        if (sourceByTile.TryGetValue(id, out int sourceId))
                            mapLayer.SetCell(new Vector2I(x, y), sourceId, Vector2I.Zero);
                    }
                }

                root.AddChild(mapLayer);
            }

            return root;
        }
    }
}
