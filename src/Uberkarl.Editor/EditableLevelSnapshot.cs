using Uberkarl.Behavior;
using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>
/// Projects an <see cref="EditableLevel"/> into the runtime <see cref="ResolvedLevel"/> shape so the
/// editor canvas can render it through the same <c>TileMapLevelBuilder</c> the play scene uses — one
/// rendering path for authoring and play. This is a read-only snapshot of the current cells; after the
/// initial build the canvas updates individual cells from the <see cref="CellChange"/> values the
/// session returns rather than rebuilding.
/// </summary>
public static class EditableLevelSnapshot
{
    public static ResolvedLevel ToResolvedLevel(EditableLevel level)
    {
        if (level is null)
            throw new ArgumentNullException(nameof(level));

        var graphics = new Dictionary<int, byte[]>(level.Tiles.Count);
        var colliding = new HashSet<int>();
        var collisionShapes = new Dictionary<int, CollisionShapeDefinition>(level.Tiles.Count);
        var animations = new Dictionary<int, ResolvedAnimation>();
        // DiVoid #7747: keyed by tile id exactly like ResolvedLevel.TileBehaviors / LevelLoader's own
        // ResolveTileBehaviors, so EffectiveTileBehaviors() (the only correct way to enumerate scripted tile
        // cells) works identically whether the level came from a playtest snapshot or a stand-alone load.
        var tileBehaviors = new Dictionary<int, ResolvedBehaviorBinding>();
        var terrainSetIdByTerrainId = level.TerrainSets
            .SelectMany(set => set.Terrains.Select(terrain => (TerrainId: terrain.Id, TerrainSetId: set.Id)))
            .ToDictionary(pair => pair.TerrainId, pair => pair.TerrainSetId);
        var tileTerrains = new Dictionary<int, ResolvedTileTerrain>();
        foreach (var tile in level.Tiles)
        {
            graphics[tile.Id] = tile.Graphic;
            collisionShapes[tile.Id] = tile.CollisionShape;
            if (tile.CollisionShape.Kind != CollisionShapeKind.None)
                colliding.Add(tile.Id);
            if (tile.Behavior is { } behavior)
                tileBehaviors[tile.Id] = behavior;

            // DiVoid #7551 Phase 2: the live canvas preview must resolve animated tiles the SAME way
            // LevelLoader does at runtime (frame 0 = the tile's own graphic, then its extra frames in
            // order) — otherwise author-sees != player-gets (design #7580 §9), which is exactly what this
            // snapshot exists to prevent for every other tile property.
            if (tile.IsAnimated)
            {
                var frames = new List<byte[]>(tile.Frames.Count + 1) { tile.Graphic };
                foreach (var frame in tile.Frames)
                    frames.Add(frame.Graphic);
                animations[tile.Id] = new ResolvedAnimation { Frames = frames, Speed = tile.AnimationSpeed };
            }

            // DiVoid #7551 Phase 3: same requirement, for terrain membership — the P2 bug (the snapshot
            // dropping new data so the canvas preview silently diverged from the runtime) is exactly what
            // this feeds forward: TileSetBuilder/TileMapLevelBuilder need TerrainSets/TileTerrains from the
            // SAME snapshot the canvas and the playtest overlay both build through.
            if (tile.Terrain is { } terrainId && terrainSetIdByTerrainId.TryGetValue(terrainId, out var terrainSetId))
                tileTerrains[tile.Id] = new ResolvedTileTerrain { TerrainSetId = terrainSetId, TerrainId = terrainId, PeeringBits = tile.PeeringBits };
        }

        var terrainSets = level.TerrainSets
            .Select(set => new ResolvedTerrainSet
            {
                Id = set.Id,
                Name = set.Name,
                MatchingMode = set.MatchingMode,
                Terrains = set.Terrains
                    .Select(terrain => new ResolvedTerrain
                    {
                        Id = terrain.Id,
                        Name = terrain.Name,
                        Color = RgbaColor.TryParse(terrain.Color, out var color) ? color : null,
                        DefaultTileId = terrain.DefaultTile,
                    })
                    .ToArray(),
            })
            .ToArray();

        RgbaColor? background = null;
        if (!string.IsNullOrWhiteSpace(level.BackgroundColor) && RgbaColor.TryParse(level.BackgroundColor, out var parsed))
            background = parsed;

        var layers = level.Layers
            .Select(layer => new ResolvedLayer
            {
                Name = layer.Name,
                Collision = layer.Collision,
                ScrollSpeed = layer.ScrollSpeed,
                Repeat = layer.Repeat,
                Cells = layer.Cells.ToArray(),
                Terrain = layer.Terrain.ToArray(),
            })
            .ToArray();

        return new ResolvedLevel
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            BackgroundColor = background,
            Layers = layers,
            TileGraphics = graphics,
            CollidingTileIds = colliding,
            TileCollisionShapes = collisionShapes,
            TileAnimations = animations,
            TerrainSets = terrainSets,
            TileTerrains = tileTerrains,
            Spawns = new Dictionary<string, GridPosition>(level.Spawns),
            DefaultSpawn = level.DefaultSpawn,
            // DiVoid #7747: without these three, a level played via PlaytestOverlay (which is fed exactly
            // this snapshot) always looked like it declared zero scripted tiles/triggers/level-script,
            // regardless of what the package actually authored — the HUD would show (it does not depend on
            // any of this) but touching a scripted tile (e.g. the demo hurt-on-contact spike) did nothing,
            // because EffectiveTileBehaviors() had nothing to yield. LevelPlay's stand-alone LevelLoader.Load
            // path was never affected — this gap was specific to the editor's projection.
            TileBehaviors = tileBehaviors,
            TileBehaviorOverrides = level.TileBehaviorOverrides,
            Triggers = level.Triggers,
            LevelScript = level.LevelScript,
        };
    }
}
