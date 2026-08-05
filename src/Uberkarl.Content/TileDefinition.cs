using Uberkarl.Packages;

namespace Uberkarl.Content;

public sealed class TileDefinition
{
    /// <summary>Animation speed (frames per second) a freshly-authored animated tile starts at when the author has not chosen one yet (DiVoid #7551 Phase 2, design #7580). Godot's <c>TileSetAtlasSource.SetTileAnimationSpeed</c> unit.</summary>
    public const double DefaultAnimationSpeed = 5.0;

    public int Id { get; init; }

    /// <summary>
    /// Optional author-facing display name (DiVoid #7551 tileset authoring — named via the on-screen
    /// keyboard). Purely cosmetic: placement and identity are always by <see cref="Id"/>. Omitted from
    /// JSON when unset so pre-authoring content loads unchanged.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The tile's graphic. For a simple tile this is its only frame; for an <see cref="IsAnimated"/> tile
    /// this is animation frame 0 — <see cref="Frames"/> carries frames 1..N (design #7580 §7). Keeping the
    /// primary graphic on this always-present field (rather than folding frame 0 into a list) means every
    /// existing single-graphic consumer (thumbnails, package contributions, the loader's tile→bytes map)
    /// keeps working unchanged for both simple and animated tiles.
    /// </summary>
    public ResourceReference Graphic { get; init; }

    /// <summary>
    /// This tile's collision footprint (DiVoid #7551 Phase 4, design #7580 §7) — replaces the old
    /// <c>Collides</c> boolean (which only ever expressed "the whole tile" or "nothing"). Collision is a
    /// property of the tile, but it is only enforced when the tile is placed on a layer whose
    /// <see cref="LayerDefinition.Collision"/> is true. Per-frame collision is not a thing — an animated
    /// tile's collision is stable across all its frames (design #7580 §11 — "an animated tile's collision
    /// must be stable across frames; frames are visual only"). Defaults to
    /// <see cref="CollisionShapeDefinition.None"/> (no collision), matching the old bool's default of
    /// <c>false</c>. See <see cref="CollisionShapeResolver"/> for turning this into concrete polygon points.
    /// </summary>
    public CollisionShapeDefinition CollisionShape { get; init; } = CollisionShapeDefinition.None;

    /// <summary>
    /// Ordered animation frames AFTER <see cref="Graphic"/> (which is always frame 0). Empty/omitted for a
    /// simple tile. Tile "kind" is structural, not an enum (design #7580 §7/§10): a non-empty
    /// <see cref="Frames"/> is what makes a tile animated — <see cref="IsAnimated"/> reads exactly this.
    /// Omitted from JSON when empty so pre-Phase-2 content (every tile simple) loads unchanged.
    /// </summary>
    public IReadOnlyList<ResourceReference> Frames { get; init; } = Array.Empty<ResourceReference>();

    /// <summary>
    /// Animation playback speed in frames per second — Godot's <c>TileSetAtlasSource</c> per-tile
    /// animation speed unit, applied verbatim by <c>TileSetBuilder</c>. Only meaningful when
    /// <see cref="IsAnimated"/>; ignored for a simple tile.
    /// </summary>
    public double AnimationSpeed { get; init; } = DefaultAnimationSpeed;

    /// <summary>
    /// Structural tile kind (design #7580 §7 — "no enum migration"): true when this tile carries at least
    /// one frame beyond <see cref="Graphic"/>. A tile is simple XOR animated; there is no separate flag to
    /// keep in sync with <see cref="Frames"/>.
    /// </summary>
    public bool IsAnimated => Frames.Count > 0;

    /// <summary>
    /// Which declared <see cref="TerrainDefinition.Id"/> (from the owning tile set's
    /// <see cref="TileSetDefinition.TerrainSets"/>) this tile is a variant of, or <c>null</c> for a plain
    /// tile that never participates in terrain auto-tiling (DiVoid #7551 Phase 3, design #7580 §7). Omitted
    /// from JSON when null so pre-Phase-3 content loads unchanged. A tile may be simple-or-animated AND a
    /// terrain variant at the same time — terrain membership is orthogonal to animation (design #7580 §7,
    /// "kind is structural").
    /// </summary>
    public int? Terrain { get; init; }

    /// <summary>
    /// Which of this variant's eight neighbours must belong to the SAME terrain for Godot's terrain-connect
    /// resolution to pick this specific tile (DiVoid #7551 Phase 3, design #7580 §14). Only meaningful when
    /// <see cref="Terrain"/> is set; <see cref="TerrainPeering.None"/> (the default) for a non-terrain tile.
    /// </summary>
    public TerrainPeering PeeringBits { get; init; } = TerrainPeering.None;
}
