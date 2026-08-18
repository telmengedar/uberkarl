namespace Uberkarl.Content;

/// <summary>The authored-content rules for a tile behavior override, shared by every door into the runtime shape.</summary>
public static class TileBehaviorOverrideRules
{
    /// <summary>Throws <see cref="LevelContentException"/> if <paramref name="entry"/> cannot apply to a level of the given shape.</summary>
    public static void Validate(TileBehaviorOverride entry, int layerCount, int width, int height)
    {
        if (entry.Layer < 0 || entry.Layer >= layerCount)
            throw new LevelContentException($"Tile behavior override references layer {entry.Layer}, but the level has {layerCount} layer(s).");
        if (entry.Cell.X < 0 || entry.Cell.Y < 0 || entry.Cell.X >= width || entry.Cell.Y >= height)
            throw new LevelContentException($"Tile behavior override cell ({entry.Cell.X},{entry.Cell.Y}) is outside the {width}x{height} grid.");
        if (entry.Binding is not null && entry.Removed)
            throw new LevelContentException($"Tile behavior override at layer {entry.Layer} cell ({entry.Cell.X},{entry.Cell.Y}) declares both a replacement binding and 'removed' — exactly one is allowed.");
        if (entry.Binding is null && !entry.Removed)
            throw new LevelContentException($"Tile behavior override at layer {entry.Layer} cell ({entry.Cell.X},{entry.Cell.Y}) declares neither a binding nor 'removed'.");
    }

    /// <summary>The message used when the same (layer, cell) is overridden more than once.</summary>
    public static string DuplicateMessage(TileBehaviorOverride entry)
        => $"Tile behavior override at layer {entry.Layer} cell ({entry.Cell.X},{entry.Cell.Y}) is defined more than once.";
}
