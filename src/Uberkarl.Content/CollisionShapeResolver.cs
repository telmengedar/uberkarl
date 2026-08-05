namespace Uberkarl.Content;

/// <summary>
/// Resolves a <see cref="CollisionShapeDefinition"/> into concrete polygon points, normalized to the unit
/// tile square — (0,0) = top-left, (1,1) = bottom-right (DiVoid #7551 Phase 4, design #7580 §14/Phase-4).
/// Pure geometry, Godot-free, and unit-testable on its own — <c>TileSetBuilder</c> (the only Godot-side
/// consumer) is the single place these points are scaled by the level's actual pixel tile size and offset
/// to match the existing full-tile collision polygon's own centering (each point's normalized coordinate
/// maps to <c>(coordinate - 0.5) * tileSize</c>, matching <c>TileSetBuilder</c>'s previous hard-coded
/// full-square points). TileSetBuilder does no shape-specific geometry of its own beyond that final pixel
/// scale — every shape family (full/rect/polygon/preset) is resolved here.
/// </summary>
public static class CollisionShapeResolver
{
    static readonly IReadOnlyList<CollisionPointDefinition> NoPoints = Array.Empty<CollisionPointDefinition>();

    static readonly IReadOnlyList<CollisionPointDefinition> FullTilePoints = RectPoints(0f, 0f, 1f, 1f);

    /// <summary>
    /// Returns <paramref name="shape"/>'s polygon points, normalized 0..1 within the tile, in a consistent
    /// top-left-first winding. Empty for <see cref="CollisionShapeKind.None"/> (no collision) — the caller
    /// should skip adding a collision polygon entirely for an empty result, exactly as it always has for a
    /// non-colliding tile.
    /// </summary>
    public static IReadOnlyList<CollisionPointDefinition> ResolvePoints(CollisionShapeDefinition shape)
    {
        if (shape is null)
            throw new ArgumentNullException(nameof(shape));

        return shape.Kind switch
        {
            CollisionShapeKind.None => NoPoints,
            CollisionShapeKind.Full => FullTilePoints,
            CollisionShapeKind.Rect => RectPoints(shape.RectX, shape.RectY, shape.RectWidth, shape.RectHeight),
            CollisionShapeKind.Polygon => shape.Points,
            CollisionShapeKind.Preset => ResolvePreset(RequirePreset(shape)),
            _ => throw new LevelContentException($"Unknown collision shape kind '{shape.Kind}'."),
        };
    }

    static CollisionPreset RequirePreset(CollisionShapeDefinition shape)
        => shape.Preset ?? throw new LevelContentException("A preset collision shape must name a preset.");

    static CollisionPointDefinition[] RectPoints(float x, float y, float width, float height) => new[]
    {
        new CollisionPointDefinition(x, y),
        new CollisionPointDefinition(x + width, y),
        new CollisionPointDefinition(x + width, y + height),
        new CollisionPointDefinition(x, y + height),
    };

    // Named presets (design #7580 Phase 4 — the gamepad-authorable shape set): half-tiles are simple rects;
    // slopes are right triangles whose hypotenuse runs corner-to-corner, named for which side is the HIGH
    // side (the side where collision extends furthest up) — SlopeLeft's high side is the tile's LEFT edge
    // (a player walking left climbs it, walking right descends it); SlopeRight's high side is the RIGHT
    // edge.
    static IReadOnlyList<CollisionPointDefinition> ResolvePreset(CollisionPreset preset) => preset switch
    {
        CollisionPreset.TopHalf => RectPoints(0f, 0f, 1f, 0.5f),
        CollisionPreset.BottomHalf => RectPoints(0f, 0.5f, 1f, 0.5f),
        CollisionPreset.LeftHalf => RectPoints(0f, 0f, 0.5f, 1f),
        CollisionPreset.RightHalf => RectPoints(0.5f, 0f, 0.5f, 1f),
        CollisionPreset.SlopeLeft => new[]
        {
            new CollisionPointDefinition(0f, 0f), // top-left: the high point
            new CollisionPointDefinition(1f, 1f), // bottom-right: the low point
            new CollisionPointDefinition(0f, 1f), // bottom-left
        },
        CollisionPreset.SlopeRight => new[]
        {
            new CollisionPointDefinition(0f, 1f), // bottom-left
            new CollisionPointDefinition(1f, 1f), // bottom-right
            new CollisionPointDefinition(1f, 0f), // top-right: the high point
        },
        _ => throw new LevelContentException($"Unknown collision preset '{preset}'."),
    };
}
