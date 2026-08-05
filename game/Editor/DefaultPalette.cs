using System.Collections.Generic;
using Godot;
using Uberkarl.Content;
using Uberkarl.Editor;
using Uberkarl.Packages;

namespace Uberkarl {

    /// <summary>
    /// Builds the starter tile palette a freshly-created ("New") level opens with. Tile-set editing is a
    /// later increment, so a new level needs a small ready-made set of tiles to paint with. Each tile is
    /// a solid colour with a darker edge (the same look the sample content uses), encoded to PNG here on
    /// the engine side — the engine-agnostic model never encodes images, it only stores the bytes.
    /// </summary>
    public static class DefaultPalette {

        static readonly (int Id, string File, byte R, byte G, byte B, bool Solid)[] Entries = {
            (1, "tiles/grass.png", 78, 168, 66, true),
            (2, "tiles/dirt.png", 138, 92, 52, true),
            (3, "tiles/stone.png", 128, 130, 138, true),
            (4, "tiles/brick.png", 190, 74, 60, true),
            (5, "tiles/water.png", 64, 122, 210, false),
        };

        public static IReadOnlyList<EditableTile> Build(int tileSize) {
            List<EditableTile> tiles = new List<EditableTile>(Entries.Length);
            foreach (var entry in Entries) {
                byte[] png = EncodeSolidTile(tileSize, entry.R, entry.G, entry.B);
                CollisionShapeDefinition collisionShape = entry.Solid ? CollisionShapeDefinition.Full : CollisionShapeDefinition.None;
                tiles.Add(new EditableTile(entry.Id, ResourcePath.Create(entry.File), png, collisionShape));
            }

            return tiles;
        }

        static byte[] EncodeSolidTile(int size, byte r, byte g, byte b) {
            Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            Color fill = new Color(r / 255f, g / 255f, b / 255f);
            Color edge = new Color(fill.R * 0.6f, fill.G * 0.6f, fill.B * 0.6f);
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    bool isEdge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    image.SetPixel(x, y, isEdge ? edge : fill);
                }
            }

            return image.SavePngToBuffer();
        }
    }
}
