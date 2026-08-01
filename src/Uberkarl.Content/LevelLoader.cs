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

        var layers = new List<ResolvedLayer>(level.Layers.Count);
        foreach (var layer in level.Layers)
        {
            ValidateLayer(level, layer, graphics.Keys);
            layers.Add(new ResolvedLayer { Name = layer.Name, Cells = layer.Cells });
        }

        return new ResolvedLevel
        {
            TileSize = level.TileSize,
            Width = level.Width,
            Height = level.Height,
            Layers = layers,
            TileGraphics = graphics,
        };
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

    private static void ValidateLayer(LevelDefinition level, LayerDefinition layer, IReadOnlyCollection<int> knownTileIds)
    {
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
