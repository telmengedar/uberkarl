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
        /// Builds a Godot <see cref="TileSet"/> from resolved tile graphics, which tile ids collide, and
        /// which tile ids animate — the tile-set data a <see cref="Uberkarl.Content.ResolvedLevel"/>
        /// carries today. Godot-side only; the caller supplies primitives, not any <c>Uberkarl.Content</c>
        /// resolution.
        ///
        /// <b>Animation</b> (DiVoid #7551 Phase 2, design #7580): a tile id present in
        /// <paramref name="tileAnimations"/> gets a <see cref="TileSetAtlasSource"/> built from ALL its
        /// frames stitched into one horizontal strip texture (Godot's own animation model —
        /// <c>SetTileAnimationColumns</c> lays frames left-to-right in a single atlas image; there is no
        /// per-frame texture list), with <c>SetTileAnimationFramesCount</c> + <c>SetTileAnimationSpeed</c>
        /// set so Godot plays it natively — no per-frame C#. Since this builder is shared by both the
        /// runtime (<see cref="TileMapLevelBuilder.Build"/>) and the editor canvas
        /// (<see cref="TileMapLevelBuilder.BuildEditable"/>), the SAME animated <see cref="TileSet"/> is
        /// what both render — author-sees = player-gets (design #7580 §9). A tile's collision (if any)
        /// still applies to the base tile, stable across every frame — frames are visual only (design
        /// #7580 §11).
        /// </summary>
        public static BuiltTileSet Build(
            IReadOnlyDictionary<int, byte[]> tileGraphics,
            IReadOnlySet<int> collidingTileIds,
            IReadOnlyDictionary<int, ResolvedAnimation> tileAnimations,
            int tileSize) {
            TileSet tileSet = new TileSet { TileSize = new Vector2I(tileSize, tileSize) };
            tileSet.AddPhysicsLayer();

            Dictionary<int, int> sourceByTile = new Dictionary<int, int>();
            foreach (KeyValuePair<int, byte[]> graphic in tileGraphics) {
                int tileId = graphic.Key;
                TileSetAtlasSource source = tileAnimations.TryGetValue(tileId, out ResolvedAnimation animation)
                    ? BuildAnimatedSource(tileId, animation, tileSize)
                    : BuildSimpleSource(tileId, graphic.Value, tileSize);

                sourceByTile[tileId] = tileSet.AddSource(source);

                if (collidingTileIds.Contains(tileId))
                    AddFullTileCollision(source, tileSize);
            }

            return new BuiltTileSet(tileSet, sourceByTile);
        }

        static TileSetAtlasSource BuildSimpleSource(int tileId, byte[] png, int tileSize) {
            ImageTexture texture = ImageTexture.CreateFromImage(LoadImage(tileId, png));
            TileSetAtlasSource source = new TileSetAtlasSource {
                Texture = texture,
                TextureRegionSize = new Vector2I(tileSize, tileSize),
            };
            source.CreateTile(Vector2I.Zero);
            return source;
        }

        static TileSetAtlasSource BuildAnimatedSource(int tileId, ResolvedAnimation animation, int tileSize) {
            int frameCount = animation.Frames.Count;
            Image strip = Image.CreateEmpty(tileSize * frameCount, tileSize, false, Image.Format.Rgba8);
            for (int frame = 0; frame < frameCount; frame++) {
                Image frameImage = LoadImage(tileId, animation.Frames[frame]);
                // Godot's Image.BlitRect requires the source and destination images to share the exact
                // same pixel format and silently no-ops (leaving the destination region untouched, i.e.
                // the strip's transparent-black default) when they don't. PNGs decode to whatever format
                // their own pixel data implies (Rgb8 for an opaque source, L8/LA8 for grayscale, etc.) —
                // frame 0's graphic and a newly-imported frame commonly land on different formats, so
                // without normalizing, one frame of the animation renders as an empty/blank tile (the bug
                // Toni reported: "the second frame is empty"). Force every frame to the strip's own Rgba8
                // format before blitting so the copy always actually happens.
                if (frameImage.GetFormat() != Image.Format.Rgba8)
                    frameImage.Convert(Image.Format.Rgba8);
                strip.BlitRect(frameImage, new Rect2I(Vector2I.Zero, new Vector2I(tileSize, tileSize)), new Vector2I(frame * tileSize, 0));
            }

            ImageTexture texture = ImageTexture.CreateFromImage(strip);
            TileSetAtlasSource source = new TileSetAtlasSource {
                Texture = texture,
                TextureRegionSize = new Vector2I(tileSize, tileSize),
            };
            source.CreateTile(Vector2I.Zero);
            source.SetTileAnimationColumns(Vector2I.Zero, frameCount);
            source.SetTileAnimationFramesCount(Vector2I.Zero, frameCount);
            source.SetTileAnimationSpeed(Vector2I.Zero, (float)animation.Speed);
            return source;
        }

        static Image LoadImage(int tileId, byte[] png) {
            Image image = new Image();
            Error status = image.LoadPngFromBuffer(png);
            if (status != Error.Ok)
                throw new LevelContentException($"Tile {tileId} graphic is not a readable PNG (Godot error {status}).");
            return image;
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
