using System.Collections.Generic;
using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Builds the shared Godot <see cref="TileSet"/> a level's layers are placed onto (DiVoid #7551 Phase
    /// 1a — split out of <see cref="TileMapLevelBuilder"/>, pure refactor, behaviour identical). One atlas
    /// source per tile graphic, a single physics layer, and a full-tile collision polygon on every
    /// colliding tile id — exactly what <c>TileMapLevelBuilder.BuildTileSet</c> did inline before this
    /// split. Extracted as its own single-responsibility unit so the tile set half of level-building can
    /// grow (animation, terrains — later phases per design #7580) without <c>TileMapLevelBuilder</c> doing
    /// double duty, and so the editor's <c>TileSetEditor</c> can reuse the SAME builder for a live preview
    /// without also building a level (design #7580 §9 — "editor preview must use the same TileSetBuilder…
    /// else author-sees ≠ player-gets").
    /// </summary>
    public static class TileSetBuilder {

        /// <summary>
        /// Builds a Godot <see cref="TileSet"/> from resolved tile graphics + which tile ids collide — the
        /// exact two pieces of tile-set data a <see cref="Uberkarl.Content.ResolvedLevel"/> carries today.
        /// Godot-side only; the caller supplies primitives, not any <c>Uberkarl.Content</c> resolution.
        /// </summary>
        public static BuiltTileSet Build(IReadOnlyDictionary<int, byte[]> tileGraphics, IReadOnlySet<int> collidingTileIds, int tileSize) {
            TileSet tileSet = new TileSet { TileSize = new Vector2I(tileSize, tileSize) };
            tileSet.AddPhysicsLayer();

            Dictionary<int, int> sourceByTile = new Dictionary<int, int>();
            foreach (KeyValuePair<int, byte[]> graphic in tileGraphics) {
                Image image = new Image();
                Error status = image.LoadPngFromBuffer(graphic.Value);
                if (status != Error.Ok)
                    throw new LevelContentException($"Tile {graphic.Key} graphic is not a readable PNG (Godot error {status}).");

                ImageTexture texture = ImageTexture.CreateFromImage(image);
                TileSetAtlasSource source = new TileSetAtlasSource {
                    Texture = texture,
                    TextureRegionSize = new Vector2I(tileSize, tileSize),
                };
                source.CreateTile(Vector2I.Zero);
                sourceByTile[graphic.Key] = tileSet.AddSource(source);

                if (collidingTileIds.Contains(graphic.Key))
                    AddFullTileCollision(source, tileSize);
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

        /// <summary>The built Godot <see cref="TileSet"/> plus the tile-id → atlas-source-id map placement needs.</summary>
        public readonly struct BuiltTileSet {
            public BuiltTileSet(TileSet set, Dictionary<int, int> sourceByTile) {
                Set = set;
                SourceByTile = sourceByTile;
            }

            public TileSet Set { get; }

            public Dictionary<int, int> SourceByTile { get; }
        }
    }
}
