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
        var collidingTileIds = CollectCollidingTileIds(tileSet);
        var animations = ResolveAnimations(resolver, tileSet, graphics);
        ValidateSpawns(level);
        var backgroundColor = ParseBackgroundColor(level);

        var layers = new List<ResolvedLayer>(level.Layers.Count);
        foreach (var layer in level.Layers)
        {
            ValidateLayer(level, layer, graphics.Keys);
            layers.Add(new ResolvedLayer
            {
                Name = layer.Name,
                Collision = layer.Collision,
                ScrollSpeed = layer.ScrollSpeed,
                Repeat = layer.Repeat,
                Cells = layer.Cells,
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
            TileAnimations = animations,
            Spawns = level.Spawns,
            DefaultSpawn = level.DefaultSpawn,
            BackgroundColor = backgroundColor,
        };
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

    private static HashSet<int> CollectCollidingTileIds(TileSetDefinition tileSet)
    {
        var colliding = new HashSet<int>();
        foreach (var tile in tileSet.Tiles)
        {
            if (tile.Collides)
                colliding.Add(tile.Id);
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
