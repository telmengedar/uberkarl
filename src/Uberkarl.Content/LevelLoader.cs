using Uberkarl.Behavior;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Content;

public static class LevelLoader
{
    public static ResolvedLevel Load(IResourceResolver resolver, ResourceReference levelReference)
    {
        if (resolver is null)
            throw new ArgumentNullException(nameof(resolver));

        var level = LevelContentSerializer.ReadLevel(Resolve(resolver, levelReference, "level"));
        ValidateDimensions(level);

        var tileSet = LevelContentSerializer.ReadTileSet(Resolve(resolver, level.TileSet, "tile set"));
        var graphics = ResolveGraphics(resolver, tileSet);
        var tileCollisionShapes = ResolveCollisionShapes(tileSet);
        var collidingTileIds = CollectCollidingTileIds(tileCollisionShapes);
        var animations = ResolveAnimations(resolver, tileSet, graphics);
        var terrainSets = ResolveTerrainSets(tileSet);
        var declaredTerrainIds = CollectDeclaredTerrainIds(terrainSets);
        var tileTerrains = ResolveTileTerrains(tileSet, declaredTerrainIds);
        ValidateSpawns(level);
        var backgroundColor = ParseBackgroundColor(level);
        var tileBehaviors = ResolveTileBehaviors(resolver, tileSet);
        var tileBehaviorOverrides = ResolveTileBehaviorOverrides(resolver, level);
        var triggers = ResolveTriggers(resolver, level);
        var levelScript = level.LevelScript is { } levelScriptBinding ? BehaviorBindingResolver.Resolve(resolver, levelScriptBinding) : null;

        var layers = new List<ResolvedLayer>(level.Layers.Count);
        foreach (var layer in level.Layers)
        {
            ValidateLayer(level, layer, graphics.Keys);
            var terrain = ValidateAndResolveTerrainChannel(level, layer, declaredTerrainIds);
            layers.Add(new ResolvedLayer
            {
                Name = layer.Name,
                Collision = layer.Collision,
                ScrollSpeed = layer.ScrollSpeed,
                Repeat = layer.Repeat,
                Cells = layer.Cells,
                Terrain = terrain,
            });
        }

        return new ResolvedLevel
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            Layers = layers,
            TileGraphics = graphics,
            CollidingTileIds = collidingTileIds,
            TileCollisionShapes = tileCollisionShapes,
            TileAnimations = animations,
            TerrainSets = terrainSets,
            TileTerrains = tileTerrains,
            Spawns = level.Spawns,
            DefaultSpawn = level.DefaultSpawn,
            BackgroundColor = backgroundColor,
            TileBehaviors = tileBehaviors,
            TileBehaviorOverrides = tileBehaviorOverrides,
            Triggers = triggers,
            LevelScript = levelScript,
        };
    }

    /// <summary>Resolves each declared tile's default behavior binding (DiVoid #7738, design #7704 §6), keyed by tile id. A tile with no <see cref="TileDefinition.Behavior"/> contributes no entry.</summary>
    private static Dictionary<int, ResolvedBehaviorBinding> ResolveTileBehaviors(IResourceResolver resolver, TileSetDefinition tileSet)
    {
        var behaviors = new Dictionary<int, ResolvedBehaviorBinding>();
        foreach (var tile in tileSet.Tiles)
        {
            if (tile.Behavior is { } binding)
                behaviors[tile.Id] = ResolveBinding(resolver, binding, $"tile {tile.Id} behavior");
        }

        return behaviors;
    }

    /// <summary>
    /// Resolves and validates the level's sparse tile-behavior override map (DiVoid #7738, design #7704 §6):
    /// every entry's layer index and cell must be in bounds, exactly one of <see cref="TileBehaviorOverride.Binding"/>/
    /// <see cref="TileBehaviorOverride.Removed"/> must be set, and no (layer,cell) pair may repeat (it is a
    /// dictionary key — a repeat is necessarily an authoring mistake, not two independent instructions).
    /// </summary>
    private static Dictionary<(int Layer, GridPosition Cell), ResolvedBehaviorBinding?> ResolveTileBehaviorOverrides(IResourceResolver resolver, LevelDefinition level)
    {
        var overrides = new Dictionary<(int Layer, GridPosition Cell), ResolvedBehaviorBinding?>();
        foreach (var entry in level.TileBehaviorOverrides)
        {
            if (entry.Layer < 0 || entry.Layer >= level.Layers.Count)
                throw new LevelContentException($"Tile behavior override references layer {entry.Layer}, but the level has {level.Layers.Count} layer(s).");
            if (entry.Cell.X < 0 || entry.Cell.Y < 0 || entry.Cell.X >= level.Width || entry.Cell.Y >= level.Height)
                throw new LevelContentException($"Tile behavior override cell ({entry.Cell.X},{entry.Cell.Y}) is outside the {level.Width}x{level.Height} grid.");
            if (entry.Binding is not null && entry.Removed)
                throw new LevelContentException($"Tile behavior override at layer {entry.Layer} cell ({entry.Cell.X},{entry.Cell.Y}) declares both a replacement binding and 'removed' — exactly one is allowed.");
            if (entry.Binding is null && !entry.Removed)
                throw new LevelContentException($"Tile behavior override at layer {entry.Layer} cell ({entry.Cell.X},{entry.Cell.Y}) declares neither a binding nor 'removed'.");

            var key = (entry.Layer, entry.Cell);
            if (!overrides.TryAdd(key, entry.Removed ? null : ResolveBinding(resolver, entry.Binding!, $"tile behavior override at layer {entry.Layer} cell ({entry.Cell.X},{entry.Cell.Y})")))
                throw new LevelContentException($"Tile behavior override at layer {entry.Layer} cell ({entry.Cell.X},{entry.Cell.Y}) is defined more than once.");
        }

        return overrides;
    }

    /// <summary>Resolves and validates the level's area triggers (DiVoid #7738, design #7704 §6): each rect must have a positive size and lie fully within the level grid.</summary>
    private static List<ResolvedAreaTrigger> ResolveTriggers(IResourceResolver resolver, LevelDefinition level)
    {
        var triggers = new List<ResolvedAreaTrigger>(level.Triggers.Count);
        foreach (var trigger in level.Triggers)
        {
            if (trigger.Width <= 0 || trigger.Height <= 0)
                throw new LevelContentException($"Trigger '{trigger.Name}' has a non-positive size ({trigger.Width}x{trigger.Height}).");
            if (trigger.X < 0 || trigger.Y < 0 || trigger.X + trigger.Width > level.Width || trigger.Y + trigger.Height > level.Height)
                throw new LevelContentException(
                    $"Trigger '{trigger.Name}' rect ({trigger.X},{trigger.Y},{trigger.Width}x{trigger.Height}) does not fit within the {level.Width}x{level.Height} grid.");

            triggers.Add(new ResolvedAreaTrigger
            {
                Name = trigger.Name,
                X = trigger.X,
                Y = trigger.Y,
                Width = trigger.Width,
                Height = trigger.Height,
                Binding = ResolveBinding(resolver, trigger.Binding, $"trigger '{trigger.Name}'"),
            });
        }

        return triggers;
    }

    private static ResolvedBehaviorBinding ResolveBinding(IResourceResolver resolver, BehaviorBinding binding, string role)
    {
        try
        {
            return BehaviorBindingResolver.Resolve(resolver, binding);
        }
        catch (Exception exception) when (exception is UnresolvedReferenceException or ResourceNotFoundException)
        {
            throw new LevelContentException($"The {role} script resource could not be resolved.", exception);
        }
    }

    private static RgbaColor? ParseBackgroundColor(LevelDefinition level)
    {
        if (string.IsNullOrWhiteSpace(level.BackgroundColor))
            return null;
        if (!RgbaColor.TryParse(level.BackgroundColor, out var color))
            throw new LevelContentException(
                $"Background colour '{level.BackgroundColor}' is not a valid hex colour " +
                "(expected #RRGGBB or #RRGGBBAA).");
        return color;
    }

    private static byte[] Resolve(IResourceResolver resolver, ResourceReference reference, string role)
    {
        try
        {
            return resolver.Resolve(reference);
        }
        catch (Exception exception) when (exception is UnresolvedReferenceException or ResourceNotFoundException)
        {
            throw new LevelContentException($"The {role} resource '{reference}' could not be resolved.", exception);
        }
    }

    private static void ValidateDimensions(LevelDefinition level)
    {
        if (level.TileSize <= 0)
            throw new LevelContentException($"Tile size must be positive but was {level.TileSize}.");
        if (level.Width <= 0 || level.Height <= 0)
            throw new LevelContentException($"Level dimensions must be positive but were {level.Width}x{level.Height}.");
    }

    /// <summary>
    /// Resolves every declared tile's collision shape (DiVoid #7551 Phase 4, design #7580), validating it
    /// as it goes: a <see cref="CollisionShapeKind.Rect"/> must have a positive width and height, a
    /// <see cref="CollisionShapeKind.Polygon"/> must declare at least three points, and a
    /// <see cref="CollisionShapeKind.Preset"/> must name a preset — a degenerate shape fails typed
    /// (design #7580 §8's "fails typed... if a collision polygon is degenerate") rather than reaching
    /// <c>TileSetBuilder</c> and building a broken or empty Godot collision polygon.
    /// </summary>
    private static Dictionary<int, CollisionShapeDefinition> ResolveCollisionShapes(TileSetDefinition tileSet)
    {
        var shapes = new Dictionary<int, CollisionShapeDefinition>(tileSet.Tiles.Count);
        foreach (var tile in tileSet.Tiles)
        {
            ValidateCollisionShape(tile);
            shapes[tile.Id] = tile.CollisionShape;
        }

        return shapes;
    }

    private static void ValidateCollisionShape(TileDefinition tile)
    {
        var shape = tile.CollisionShape;
        switch (shape.Kind)
        {
            case CollisionShapeKind.Rect:
                if (shape.RectWidth <= 0 || shape.RectHeight <= 0)
                    throw new LevelContentException(
                        $"Tile {tile.Id} has a rect collision shape with non-positive size ({shape.RectWidth}x{shape.RectHeight}).");
                break;
            case CollisionShapeKind.Polygon:
                if (shape.Points.Count < 3)
                    throw new LevelContentException(
                        $"Tile {tile.Id} has a polygon collision shape with only {shape.Points.Count} point(s); at least 3 are required.");
                break;
            case CollisionShapeKind.Preset:
                if (shape.Preset is null)
                    throw new LevelContentException($"Tile {tile.Id} has a preset collision shape that names no preset.");
                break;
        }
    }

    private static HashSet<int> CollectCollidingTileIds(IReadOnlyDictionary<int, CollisionShapeDefinition> tileCollisionShapes)
    {
        var colliding = new HashSet<int>();
        foreach (var (tileId, shape) in tileCollisionShapes)
        {
            if (shape.Kind != CollisionShapeKind.None)
                colliding.Add(tileId);
        }

        return colliding;
    }

    private static void ValidateSpawns(LevelDefinition level)
    {
        foreach (var (name, cell) in level.Spawns)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.X >= level.Width || cell.Y >= level.Height)
                throw new LevelContentException(
                    $"Spawn '{name}' ({cell.X},{cell.Y}) is outside the {level.Width}x{level.Height} grid.");
        }

        if (level.Spawns.Count == 0)
        {
            if (!string.IsNullOrEmpty(level.DefaultSpawn))
                throw new LevelContentException(
                    $"Default spawn '{level.DefaultSpawn}' is named but the level declares no spawns.");
            return;
        }

        if (string.IsNullOrEmpty(level.DefaultSpawn))
            throw new LevelContentException("A default spawn must be named when the level declares spawns.");
        if (!level.Spawns.ContainsKey(level.DefaultSpawn))
            throw new LevelContentException($"Default spawn '{level.DefaultSpawn}' is not one of the declared spawns.");
    }

    private static Dictionary<int, byte[]> ResolveGraphics(IResourceResolver resolver, TileSetDefinition tileSet)
    {
        var graphics = new Dictionary<int, byte[]>();
        foreach (var tile in tileSet.Tiles)
        {
            if (tile.Id == LayerDefinition.EmptyCell)
                throw new LevelContentException($"Tile id {LayerDefinition.EmptyCell} is reserved for empty cells.");
            if (!graphics.TryAdd(tile.Id, Resolve(resolver, tile.Graphic, "tile graphic")))
                throw new LevelContentException($"Tile id {tile.Id} is defined more than once.");
        }

        return graphics;
    }

    /// <summary>
    /// Resolves every animated tile's ordered frame bytes (frame 0 = <see cref="TileDefinition.Graphic"/>,
    /// already in <paramref name="graphics"/>; the rest from <see cref="TileDefinition.Frames"/>) plus its
    /// playback speed (DiVoid #7551 Phase 2, design #7580 §8 — the loader validates a positive speed and
    /// that every frame reference resolves, typed as <see cref="LevelContentException"/>, exactly like
    /// every other content-boundary failure). A simple tile (<see cref="TileDefinition.IsAnimated"/> false)
    /// contributes no entry.
    /// </summary>
    private static Dictionary<int, ResolvedAnimation> ResolveAnimations(
        IResourceResolver resolver, TileSetDefinition tileSet, IReadOnlyDictionary<int, byte[]> graphics)
    {
        var animations = new Dictionary<int, ResolvedAnimation>();
        foreach (var tile in tileSet.Tiles)
        {
            if (!tile.IsAnimated)
                continue;

            if (tile.AnimationSpeed <= 0)
                throw new LevelContentException(
                    $"Tile {tile.Id} is animated but its animation speed is {tile.AnimationSpeed}; it must be positive.");

            var frames = new List<byte[]>(tile.Frames.Count + 1) { graphics[tile.Id] };
            foreach (var frame in tile.Frames)
                frames.Add(Resolve(resolver, frame, "tile animation frame"));

            animations[tile.Id] = new ResolvedAnimation { Frames = frames, Speed = tile.AnimationSpeed };
        }

        return animations;
    }

    /// <summary>
    /// Resolves the tile set's declared terrain sets/terrains (DiVoid #7551 Phase 3, design #7580), in
    /// declaration order — the order <c>TileSetBuilder</c> maps onto Godot's index-based terrain sets.
    /// Fails typed when a terrain set id or a terrain id (unique across the WHOLE tile set, not just its own
    /// set — see <see cref="TerrainDefinition.Id"/>) repeats, a terrain's colour is present but not a valid
    /// hex string, or (DiVoid #7638) a terrain's <see cref="TerrainDefinition.DefaultTile"/> does not name a
    /// declared tile that is itself a member of THIS terrain.
    /// </summary>
    private static List<ResolvedTerrainSet> ResolveTerrainSets(TileSetDefinition tileSet)
    {
        var terrainSets = new List<ResolvedTerrainSet>(tileSet.TerrainSets.Count);
        var seenSetIds = new HashSet<int>();
        var seenTerrainIds = new HashSet<int>();

        foreach (var terrainSet in tileSet.TerrainSets)
        {
            if (!seenSetIds.Add(terrainSet.Id))
                throw new LevelContentException($"Terrain set id {terrainSet.Id} is defined more than once.");

            var terrains = new List<ResolvedTerrain>(terrainSet.Terrains.Count);
            foreach (var terrain in terrainSet.Terrains)
            {
                if (!seenTerrainIds.Add(terrain.Id))
                    throw new LevelContentException($"Terrain id {terrain.Id} is defined more than once.");

                RgbaColor? color = null;
                if (!string.IsNullOrWhiteSpace(terrain.Color))
                {
                    if (!RgbaColor.TryParse(terrain.Color, out var parsed))
                        throw new LevelContentException(
                            $"Terrain '{terrain.Name}' (id {terrain.Id}) colour '{terrain.Color}' is not a valid hex colour.");
                    color = parsed;
                }

                int? defaultTileId = null;
                if (terrain.DefaultTile is { } candidateId)
                {
                    var candidate = tileSet.Tiles.FirstOrDefault(candidateTile => candidateTile.Id == candidateId);
                    if (candidate is null)
                        throw new LevelContentException(
                            $"Terrain '{terrain.Name}' (id {terrain.Id}) default tile {candidateId} is not a declared tile.");
                    if (candidate.Terrain != terrain.Id)
                        throw new LevelContentException(
                            $"Terrain '{terrain.Name}' (id {terrain.Id}) default tile {candidateId} does not belong to this terrain.");
                    defaultTileId = candidateId;
                }

                terrains.Add(new ResolvedTerrain { Id = terrain.Id, Name = terrain.Name, Color = color, DefaultTileId = defaultTileId });
            }

            terrainSets.Add(new ResolvedTerrainSet
            {
                Id = terrainSet.Id,
                Name = terrainSet.Name,
                MatchingMode = terrainSet.MatchingMode,
                Terrains = terrains,
            });
        }

        return terrainSets;
    }

    private static HashSet<int> CollectDeclaredTerrainIds(IReadOnlyList<ResolvedTerrainSet> terrainSets)
    {
        var ids = new HashSet<int>();
        foreach (var terrainSet in terrainSets)
            foreach (var terrain in terrainSet.Terrains)
                ids.Add(terrain.Id);
        return ids;
    }

    /// <summary>
    /// Resolves which tiles are terrain variants and their peering bits (DiVoid #7551 Phase 3, design
    /// #7580). Fails typed when a tile's <see cref="TileDefinition.Terrain"/> names an undeclared terrain id
    /// (design #7580 §8 — "fails typed... if... a peering bit names an undeclared terrain").
    /// </summary>
    private static Dictionary<int, ResolvedTileTerrain> ResolveTileTerrains(TileSetDefinition tileSet, HashSet<int> declaredTerrainIds)
    {
        var terrainSetIdByTerrainId = new Dictionary<int, int>();
        foreach (var terrainSet in tileSet.TerrainSets)
            foreach (var terrain in terrainSet.Terrains)
                terrainSetIdByTerrainId[terrain.Id] = terrainSet.Id;

        var tileTerrains = new Dictionary<int, ResolvedTileTerrain>();
        foreach (var tile in tileSet.Tiles)
        {
            if (tile.Terrain is not { } terrainId)
                continue;

            if (!declaredTerrainIds.Contains(terrainId))
                throw new LevelContentException($"Tile {tile.Id} belongs to undeclared terrain id {terrainId}.");

            tileTerrains[tile.Id] = new ResolvedTileTerrain
            {
                TerrainSetId = terrainSetIdByTerrainId[terrainId],
                TerrainId = terrainId,
                PeeringBits = tile.PeeringBits,
            };
        }

        return tileTerrains;
    }

    /// <summary>
    /// Validates a layer's terrain channel (DiVoid #7551 Phase 3, design #7580 §8) and returns it ALWAYS
    /// fully populated to <c>Width*Height</c> entries (an omitted/empty <see cref="LayerDefinition.Terrain"/>
    /// resolves to every cell sentinel) — see <see cref="ResolvedLayer.Terrain"/>. Enforces: the channel, if
    /// declared, has exactly as many entries as the grid; every non-sentinel entry names a declared terrain
    /// id; and the two-channel invariant — a cell is never BOTH a concrete tile AND terrain-painted (design
    /// #7580 §7, "a cell is not both concrete and terrain-marked").
    /// </summary>
    private static IReadOnlyList<int> ValidateAndResolveTerrainChannel(LevelDefinition level, LayerDefinition layer, HashSet<int> declaredTerrainIds)
    {
        var expected = level.Width * level.Height;
        if (layer.Terrain.Count == 0)
        {
            var empty = new int[expected];
            Array.Fill(empty, LayerDefinition.EmptyCell);
            return empty;
        }

        if (layer.Terrain.Count != expected)
            throw new LevelContentException(
                $"Layer '{layer.Name}' has {layer.Terrain.Count} terrain cells but the {level.Width}x{level.Height} grid needs {expected}.");

        for (var i = 0; i < expected; i++)
        {
            var terrainId = layer.Terrain[i];
            if (terrainId == LayerDefinition.EmptyCell)
                continue;

            if (!declaredTerrainIds.Contains(terrainId))
                throw new LevelContentException($"Layer '{layer.Name}' references undefined terrain id {terrainId}.");

            if (layer.Cells[i] != LayerDefinition.EmptyCell)
                throw new LevelContentException(
                    $"Layer '{layer.Name}' cell index {i} is both a concrete tile ({layer.Cells[i]}) and terrain-painted " +
                    $"(terrain {terrainId}) — a cell must be one or the other.");
        }

        return layer.Terrain;
    }

    private static void ValidateLayer(LevelDefinition level, LayerDefinition layer, IReadOnlyCollection<int> knownTileIds)
    {
        if (layer.Collision && layer.ScrollSpeed != 1f)
            throw new LevelContentException(
                $"Layer '{layer.Name}' is a collision layer but has scrollSpeed {layer.ScrollSpeed}; a collision " +
                "layer must be world-locked (scrollSpeed == 1.0) so its on-screen position matches its world position.");

        if (layer.Collision && layer.Repeat)
            throw new LevelContentException(
                $"Layer '{layer.Name}' is a collision layer but has repeat enabled; a collision layer must not " +
                "repeat because tiling its visuals would not tile its authored collision geometry.");

        var expected = level.Width * level.Height;
        if (layer.Cells.Count != expected)
            throw new LevelContentException(
                $"Layer '{layer.Name}' has {layer.Cells.Count} cells but the {level.Width}x{level.Height} grid needs {expected}.");

        foreach (var cell in layer.Cells)
        {
            if (cell == LayerDefinition.EmptyCell)
                continue;
            if (!knownTileIds.Contains(cell))
                throw new LevelContentException($"Layer '{layer.Name}' references undefined tile id {cell}.");
        }
    }
}
