namespace Uberkarl.Content;

/// <summary>
/// Which shape family a tile's collision footprint uses (DiVoid #7551 Phase 4, design #7580 §7) —
/// replaces the old <c>TileDefinition.Collides</c> boolean, which only ever expressed "the whole tile"
/// (<see cref="Full"/>) or "nothing" (<see cref="None"/>). <see cref="Rect"/> and <see cref="Polygon"/> are
/// the free-form seams reserved for a future mouse/sprite-editor era (design #7580 Phase 4 — "DO NOT build
/// a freeform gamepad polygon editor now"); <see cref="Preset"/> is what Phase 4's gamepad-first authoring
/// actually writes (see <see cref="CollisionPreset"/>) — a named shape a preset resolves to a concrete
/// polygon at build time (<see cref="CollisionShapeResolver"/>/<c>TileSetBuilder</c>), so authoring never
/// has to hand-place points on a gamepad.
/// </summary>
public enum CollisionShapeKind
{
    /// <summary>No collision — matches the old <c>Collides:false</c>.</summary>
    None,

    /// <summary>The whole tile square — matches the old <c>Collides:true</c>.</summary>
    Full,

    /// <summary>
    /// An axis-aligned rectangle within the tile, in <see cref="CollisionShapeDefinition"/>'s normalized
    /// (0..1, top-left origin) tile-fraction units. Reserved for a future mouse-driven editor; not
    /// authorable on a gamepad this phase.
    /// </summary>
    Rect,

    /// <summary>
    /// An arbitrary polygon within the tile, in normalized (0..1) tile-fraction units. Reserved for a
    /// future mouse/sprite-editor (design #7580 Phase 4); not authorable on a gamepad this phase — the
    /// model supports it today so that future increment needs no schema change.
    /// </summary>
    Polygon,

    /// <summary>A named preset shape (see <see cref="CollisionPreset"/>) — what the gamepad-first tile-set editor actually authors this phase.</summary>
    Preset,
}

/// <summary>
/// The gamepad-authorable named collision shapes (DiVoid #7551 Phase 4, design #7580 §14/Phase-4 — "pick a
/// preset, cycle or select, gamepad-friendly"): half-tiles and slopes. <see cref="CollisionShapeResolver"/>
/// is the single place a preset resolves to concrete polygon points.
/// </summary>
public enum CollisionPreset
{
    /// <summary>The top half of the tile (a ceiling/platform you can stand under, or a low block).</summary>
    TopHalf,

    /// <summary>The bottom half of the tile (a low platform you can stand on).</summary>
    BottomHalf,

    /// <summary>The left half of the tile.</summary>
    LeftHalf,

    /// <summary>The right half of the tile.</summary>
    RightHalf,

    /// <summary>A ramp whose high side is the tile's LEFT edge, descending toward the right.</summary>
    SlopeLeft,

    /// <summary>A ramp whose high side is the tile's RIGHT edge, descending toward the left.</summary>
    SlopeRight,
}

/// <summary>
/// One point of a <see cref="CollisionShapeKind.Polygon"/> shape, normalized to the unit tile square
/// (0,0) = top-left, (1,1) = bottom-right, independent of the level's actual pixel tile size (design #7580
/// Phase 4 — geometry is authored/stored resolution-independent; <c>TileSetBuilder</c> is the only place it
/// is scaled to pixels).
/// </summary>
public readonly record struct CollisionPointDefinition(float X, float Y);

/// <summary>
/// A tile's collision footprint (DiVoid #7551 Phase 4, design #7580 §7) — replaces
/// <c>TileDefinition.Collides</c> (bool). One flat descriptor rather than a discriminated union (mirrors
/// this codebase's existing "orthogonal optional fields" style for <see cref="TileDefinition"/> itself):
/// <see cref="Kind"/> says which fields are meaningful. Geometry (<see cref="RectX"/>/Y/Width/Height,
/// <see cref="Points"/>) is normalized to the unit tile square — see <see cref="CollisionPointDefinition"/>.
/// <see cref="CollisionShapeResolver"/> turns any shape into concrete polygon points; JSON round-trips via
/// <c>Uberkarl.Content.Json.TileDefinitionJsonConverter</c>, which also migrates pre-Phase-4 content's
/// legacy <c>"collides"</c> boolean transparently (<c>true</c> → <see cref="Full"/>, <c>false</c>/absent →
/// <see cref="None"/>).
/// </summary>
public sealed class CollisionShapeDefinition
{
    /// <summary>The default, no-collision shape — matches the old <c>Collides:false</c>.</summary>
    public static readonly CollisionShapeDefinition None = new() { Kind = CollisionShapeKind.None };

    /// <summary>The whole-tile-square shape — matches the old <c>Collides:true</c>.</summary>
    public static readonly CollisionShapeDefinition Full = new() { Kind = CollisionShapeKind.Full };

    /// <summary>Which shape family this descriptor uses. Determines which of the fields below are meaningful.</summary>
    public CollisionShapeKind Kind { get; init; } = CollisionShapeKind.None;

    /// <summary>Rect origin X, normalized (0..1). Only meaningful when <see cref="Kind"/> is <see cref="CollisionShapeKind.Rect"/>.</summary>
    public float RectX { get; init; }

    /// <summary>Rect origin Y, normalized (0..1). Only meaningful when <see cref="Kind"/> is <see cref="CollisionShapeKind.Rect"/>.</summary>
    public float RectY { get; init; }

    /// <summary>Rect width, normalized (0..1). Only meaningful when <see cref="Kind"/> is <see cref="CollisionShapeKind.Rect"/>.</summary>
    public float RectWidth { get; init; }

    /// <summary>Rect height, normalized (0..1). Only meaningful when <see cref="Kind"/> is <see cref="CollisionShapeKind.Rect"/>.</summary>
    public float RectHeight { get; init; }

    /// <summary>
    /// Polygon points, normalized (0..1). Only meaningful when <see cref="Kind"/> is
    /// <see cref="CollisionShapeKind.Polygon"/> — reserved for a future mouse/sprite-editor (design #7580
    /// Phase 4); not authored by this phase's gamepad-first <c>TileSetEditor</c>.
    /// </summary>
    public IReadOnlyList<CollisionPointDefinition> Points { get; init; } = Array.Empty<CollisionPointDefinition>();

    /// <summary>Which named preset this is. Only meaningful (and required) when <see cref="Kind"/> is <see cref="CollisionShapeKind.Preset"/>.</summary>
    public CollisionPreset? Preset { get; init; }

    /// <summary>Builds a <see cref="CollisionShapeKind.Rect"/> shape from normalized (0..1) tile-fraction coordinates.</summary>
    public static CollisionShapeDefinition FromRect(float x, float y, float width, float height) => new()
    {
        Kind = CollisionShapeKind.Rect,
        RectX = x,
        RectY = y,
        RectWidth = width,
        RectHeight = height,
    };

    /// <summary>Builds a <see cref="CollisionShapeKind.Polygon"/> shape from normalized (0..1) tile-fraction points.</summary>
    public static CollisionShapeDefinition FromPolygon(IReadOnlyList<CollisionPointDefinition> points) => new()
    {
        Kind = CollisionShapeKind.Polygon,
        Points = points ?? throw new ArgumentNullException(nameof(points)),
    };

    /// <summary>Builds a <see cref="CollisionShapeKind.Preset"/> shape naming <paramref name="preset"/>.</summary>
    public static CollisionShapeDefinition FromPreset(CollisionPreset preset) => new()
    {
        Kind = CollisionShapeKind.Preset,
        Preset = preset,
    };
}
