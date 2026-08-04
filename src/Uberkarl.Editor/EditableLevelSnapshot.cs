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
        var animations = new Dictionary<int, ResolvedAnimation>();
        var terrainSetIdByTerrainId = level.TerrainSets
            .SelectMany(set => set.Terrains.Select(terrain => (TerrainId: terrain.Id, TerrainSetId: set.Id)))
            .ToDictionary(pair => pair.TerrainId, pair => pair.TerrainSetId);
        var tileTerrains = new Dictionary<int, ResolvedTileTerrain>();
        foreach (var tile in level.Tiles)
        {
            graphics[tile.Id] = tile.Graphic;
            if (tile.Collides)
                colliding.Add(tile.Id);

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
            TileAnimations = animations,
            TerrainSets = terrainSets,
            TileTerrains = tileTerrains,
            Spawns = new Dictionary<string, GridPosition>(level.Spawns),
            DefaultSpawn = level.DefaultSpawn,
        };
    }
}
