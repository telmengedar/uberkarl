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
        }

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
            Spawns = new Dictionary<string, GridPosition>(level.Spawns),
            DefaultSpawn = level.DefaultSpawn,
        };
    }
}
