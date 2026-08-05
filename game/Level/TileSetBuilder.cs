using System;
using System.Collections.Generic;
using Godot;
using Uberkarl.Content;

namespace Uberkarl {

    /// <summary>
    /// Builds the shared Godot <see cref="TileSet"/> a level's layers are placed onto (DiVoid #7551 Phase
    /// 1a — split out of <see cref="TileMapLevelBuilder"/>, pure refactor, behaviour identical). One atlas
    /// source per tile graphic, a single physics layer, and a collision polygon built from each tile's
    /// <see cref="CollisionShapeDefinition"/> (DiVoid #7551 Phase 4 — full tile, rect, explicit polygon, or
    /// a named preset's polygon; <see cref="AddCollision"/>/<see cref="CollisionShapeResolver"/>) — exactly
    /// what <c>TileMapLevelBuilder.BuildTileSet</c> did inline (as an always-full-tile square) before this
    /// split. Extracted as its own single-responsibility unit so the tile set half of level-building can
    /// grow (animation, terrains, collision shapes — later phases per design #7580) without
    /// <c>TileMapLevelBuilder</c> doing double duty, and so the editor's <c>TileSetEditor</c> can reuse the
    /// SAME builder for a live preview without also building a level (design #7580 §9 — "editor preview
    /// must use the same TileSetBuilder… else author-sees ≠ player-gets").
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
            IReadOnlyDictionary<int, CollisionShapeDefinition> tileCollisionShapes,
            IReadOnlyDictionary<int, ResolvedAnimation> tileAnimations,
            int tileSize) {
            return Build(tileGraphics, tileCollisionShapes, tileAnimations, Array.Empty<ResolvedTerrainSet>(), new Dictionary<int, ResolvedTileTerrain>(), tileSize);
        }

        /// <summary>
        /// Overload adding terrain support (DiVoid #7551 Phase 3, design #7580): <paramref name="terrainSets"/>
        /// are mapped, in declaration order, onto Godot's index-based Terrain Sets/Terrains; every tile with an
        /// entry in <paramref name="tileTerrains"/> has its atlas tile's <c>TerrainSet</c>/<c>Terrain</c> and
        /// peering bits set from that membership. This IS the meta-tile feature made real: with a variant's
        /// peering bits correctly declared here, <see cref="TileMapLevelBuilder"/>'s
        /// <c>TileMapLayer.SetCellsTerrainConnect</c> call lets Godot auto-select the matching variant from a
        /// level's logical terrain paint — no custom neighbour-pattern resolver in this codebase (design #7580
        /// §10, trade-off 2: "lean on Godot Terrain Sets... not a custom auto-tile resolver").
        /// </summary>
        public static BuiltTileSet Build(
            IReadOnlyDictionary<int, byte[]> tileGraphics,
            IReadOnlyDictionary<int, CollisionShapeDefinition> tileCollisionShapes,
            IReadOnlyDictionary<int, ResolvedAnimation> tileAnimations,
            IReadOnlyList<ResolvedTerrainSet> terrainSets,
            IReadOnlyDictionary<int, ResolvedTileTerrain> tileTerrains,
            int tileSize) {
            TileSet tileSet = new TileSet { TileSize = new Vector2I(tileSize, tileSize) };
            tileSet.AddPhysicsLayer();

            Dictionary<int, TerrainIndex> terrainIndexByTerrainId = BuildTerrainSets(tileSet, terrainSets);

            Dictionary<int, int> sourceByTile = new Dictionary<int, int>();
            foreach (KeyValuePair<int, byte[]> graphic in tileGraphics) {
                int tileId = graphic.Key;
                TileSetAtlasSource source = tileAnimations.TryGetValue(tileId, out ResolvedAnimation animation)
                    ? BuildAnimatedSource(tileId, animation, tileSize)
                    : BuildSimpleSource(tileId, graphic.Value, tileSize);

                sourceByTile[tileId] = tileSet.AddSource(source);

                if (tileCollisionShapes.TryGetValue(tileId, out CollisionShapeDefinition shape))
                    AddCollision(source, tileSize, shape);

                if (tileTerrains.TryGetValue(tileId, out ResolvedTileTerrain membership) &&
                    terrainIndexByTerrainId.TryGetValue(membership.TerrainId, out TerrainIndex index))
                    ApplyTerrainMembership(source, tileSize, index, membership.PeeringBits);
            }

            return new BuiltTileSet(tileSet, sourceByTile, terrainIndexByTerrainId);
        }

        // Adds each terrain set (in declaration order — this order IS the Godot terrain-set index) and its
        // terrains, and returns the id -> Godot-index lookup ApplyTerrainMembership and (for the caller,
        // TileMapLevelBuilder's terrain-connect calls) the level builder both need.
        static Dictionary<int, TerrainIndex> BuildTerrainSets(TileSet tileSet, IReadOnlyList<ResolvedTerrainSet> terrainSets) {
            Dictionary<int, TerrainIndex> lookup = new Dictionary<int, TerrainIndex>();
            foreach (ResolvedTerrainSet terrainSet in terrainSets) {
                int setIndex = tileSet.GetTerrainSetsCount();
                tileSet.AddTerrainSet(-1);
                tileSet.SetTerrainSetMode(setIndex, MapMatchingMode(terrainSet.MatchingMode));

                foreach (ResolvedTerrain terrain in terrainSet.Terrains) {
                    int terrainIndex = tileSet.GetTerrainsCount(setIndex);
                    tileSet.AddTerrain(setIndex, -1);
                    tileSet.SetTerrainName(setIndex, terrainIndex, terrain.Name);
                    if (terrain.Color is { } color)
                        tileSet.SetTerrainColor(setIndex, terrainIndex, new Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
                    lookup[terrain.Id] = new TerrainIndex(setIndex, terrainIndex);
                }
            }

            return lookup;
        }

        static TileSet.TerrainMode MapMatchingMode(TerrainMatchMode mode) => mode switch {
            TerrainMatchMode.Corners => TileSet.TerrainMode.Corners,
            TerrainMatchMode.Sides => TileSet.TerrainMode.Sides,
            _ => TileSet.TerrainMode.CornersAndSides,
        };

        // Assigns ONE atlas tile (always Vector2I.Zero — each of our tiles is a single-cell atlas source) to
        // its terrain set/terrain and sets the peering bits for the eight directions the variant declares
        // (design #7580 §14's 3x3 peering-bit grid, realized). A direction NOT in peeringBits is left unset
        // (Godot's "don't care" default) rather than pointed at a different terrain — this codebase's terrain
        // model doesn't (yet) express "must be a DIFFERENT specific terrain on this side", only "must be the
        // same terrain" (design #7580 §14 recommendation), which is exactly what a same-terrain-set peering
        // bit expresses.
        static void ApplyTerrainMembership(TileSetAtlasSource source, int tileSize, TerrainIndex index, TerrainPeering peeringBits) {
            TileData data = source.GetTileData(Vector2I.Zero, 0);
            data.TerrainSet = index.TerrainSet;
            data.Terrain = index.Terrain;

            foreach (KeyValuePair<TerrainPeering, TileSet.CellNeighbor> direction in DirectionByPeeringBit) {
                if ((peeringBits & direction.Key) != 0)
                    data.SetTerrainPeeringBit(direction.Value, index.Terrain);
            }
        }

        // Maps our engine-agnostic eight-direction bitmask onto Godot's square-tile CellNeighbor values.
        static readonly Dictionary<TerrainPeering, TileSet.CellNeighbor> DirectionByPeeringBit = new() {
            [TerrainPeering.North] = TileSet.CellNeighbor.TopSide,
            [TerrainPeering.NorthEast] = TileSet.CellNeighbor.TopRightCorner,
            [TerrainPeering.East] = TileSet.CellNeighbor.RightSide,
            [TerrainPeering.SouthEast] = TileSet.CellNeighbor.BottomRightCorner,
            [TerrainPeering.South] = TileSet.CellNeighbor.BottomSide,
            [TerrainPeering.SouthWest] = TileSet.CellNeighbor.BottomLeftCorner,
            [TerrainPeering.West] = TileSet.CellNeighbor.LeftSide,
            [TerrainPeering.NorthWest] = TileSet.CellNeighbor.TopLeftCorner,
        };

        /// <summary>A terrain's resolved Godot indices: which terrain SET, and which terrain within it.</summary>
        public readonly struct TerrainIndex {
            public TerrainIndex(int terrainSet, int terrain) {
                TerrainSet = terrainSet;
                Terrain = terrain;
            }

            public int TerrainSet { get; }

            public int Terrain { get; }
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

        /// <summary>
        /// Builds this tile's physics-layer collision polygon from its <see cref="CollisionShapeDefinition"/>
        /// (DiVoid #7551 Phase 4, design #7580): <see cref="CollisionShapeResolver.ResolvePoints"/> is the
        /// single, Godot-free place a shape (full/rect/polygon/preset) resolves to normalized (0..1) tile-
        /// fraction points — this method's only job is to scale those points by <paramref name="tileSize"/>
        /// and re-center them the same way the old always-full-tile square was centered (a normalized
        /// coordinate <c>c</c> maps to <c>(c - 0.5) * tileSize</c>, so (0,0)/(1,1) land on the old square's
        /// own (-half,-half)/(half,half) corners). A <see cref="CollisionShapeKind.None"/> shape resolves to
        /// zero points and adds no collision at all — identical to a non-colliding tile before this phase.
        /// </summary>
        static void AddCollision(TileSetAtlasSource source, int tileSize, CollisionShapeDefinition shape) {
            IReadOnlyList<CollisionPointDefinition> normalized = CollisionShapeResolver.ResolvePoints(shape);
            if (normalized.Count == 0)
                return;

            Vector2[] points = new Vector2[normalized.Count];
            for (int i = 0; i < normalized.Count; i++) {
                CollisionPointDefinition point = normalized[i];
                points[i] = new Vector2((point.X - 0.5f) * tileSize, (point.Y - 0.5f) * tileSize);
            }

            TileData data = source.GetTileData(Vector2I.Zero, 0);
            data.AddCollisionPolygon(0);
            data.SetCollisionPolygonPoints(0, 0, points);
        }

        /// <summary>
        /// The built Godot <see cref="TileSet"/> plus the tile-id → atlas-source-id map placement needs, plus
        /// (DiVoid #7551 Phase 3) the terrain-id → Godot-index lookup <see cref="TileMapLevelBuilder"/> needs
        /// to drive <c>TileMapLayer.SetCellsTerrainConnect</c> for a level's logical terrain paint.
        /// </summary>
        public readonly struct BuiltTileSet {
            public BuiltTileSet(TileSet set, Dictionary<int, int> sourceByTile, Dictionary<int, TerrainIndex> terrainIndexByTerrainId) {
                Set = set;
                SourceByTile = sourceByTile;
                TerrainIndexByTerrainId = terrainIndexByTerrainId;
            }

            public TileSet Set { get; }

            public Dictionary<int, int> SourceByTile { get; }

            public Dictionary<int, TerrainIndex> TerrainIndexByTerrainId { get; }
        }
    }
}
