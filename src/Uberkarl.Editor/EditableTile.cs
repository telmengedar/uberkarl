using Uberkarl.Behavior;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// A single tile in the level's palette as the editor holds it: its numeric id (what a grid cell
/// stores), the in-package path its graphic lives at, the raw graphic bytes (kept in memory so the
/// level can be re-saved without a live package handle), and its collision footprint. This is the
/// authoring shape — distinct from <see cref="Content.TileDefinition"/> (which references the graphic
/// but does not carry its bytes) and from the resolved runtime view.
/// </summary>
public sealed class EditableTile
{
    /// <summary>Mirrors <see cref="Content.TileDefinition.DefaultAnimationSpeed"/> — kept as its own constant so this authoring-side type has no compile-time dependency shape surprise on <c>Content</c> beyond what it already has via <c>LayerDefinition.EmptyCell</c>.</summary>
    public const double DefaultAnimationSpeed = Content.TileDefinition.DefaultAnimationSpeed;

    public EditableTile(
        int id, ResourcePath graphicPath, byte[] graphic, Content.CollisionShapeDefinition collisionShape, string? name = null,
        IReadOnlyList<EditableTileFrame>? frames = null, double animationSpeed = DefaultAnimationSpeed,
        int? terrain = null, Content.TerrainPeering peeringBits = Content.TerrainPeering.None,
        ResolvedBehaviorBinding? behavior = null)
    {
        if (id == Content.LayerDefinition.EmptyCell)
            throw new ArgumentException($"Tile id {Content.LayerDefinition.EmptyCell} is reserved for empty cells.", nameof(id));
        Id = id;
        GraphicPath = graphicPath;
        Graphic = graphic ?? throw new ArgumentNullException(nameof(graphic));
        CollisionShape = collisionShape ?? throw new ArgumentNullException(nameof(collisionShape));
        Name = name;
        Frames = frames ?? Array.Empty<EditableTileFrame>();
        AnimationSpeed = animationSpeed;
        Terrain = terrain;
        PeeringBits = peeringBits;
        Behavior = behavior;
    }

    /// <summary>The numeric id a grid cell stores to place this tile.</summary>
    public int Id { get; }

    /// <summary>Optional author-facing display name (DiVoid #7551 — named via the on-screen keyboard).</summary>
    public string? Name { get; }

    /// <summary>The in-package resource path this tile's graphic (animation frame 0) is stored at (preserved on save).</summary>
    public ResourcePath GraphicPath { get; }

    /// <summary>The tile graphic bytes (a PNG for the sample content) — frame 0. Held so the tile set round-trips without the source package.</summary>
    public byte[] Graphic { get; }

    /// <summary>The tile's collision footprint (DiVoid #7551 Phase 4). Only enforced on a collision layer at play time. Stable across every animation frame (design #7580 §11).</summary>
    public Content.CollisionShapeDefinition CollisionShape { get; }

    /// <summary>Ordered animation frames AFTER <see cref="Graphic"/> (DiVoid #7551 Phase 2). Empty for a simple tile — see <see cref="IsAnimated"/>.</summary>
    public IReadOnlyList<EditableTileFrame> Frames { get; }

    /// <summary>Animation playback speed in frames per second. Only meaningful when <see cref="IsAnimated"/>.</summary>
    public double AnimationSpeed { get; }

    /// <summary>Structural tile kind (design #7580 §7 — no enum): true once at least one frame has been added beyond <see cref="Graphic"/>.</summary>
    public bool IsAnimated => Frames.Count > 0;

    /// <summary>Which declared <see cref="EditableTerrain.Id"/> this tile is a variant of (DiVoid #7551 Phase 3), or <c>null</c> for a plain tile.</summary>
    public int? Terrain { get; }

    /// <summary>This variant's peering bits (only meaningful when <see cref="Terrain"/> is set).</summary>
    public Content.TerrainPeering PeeringBits { get; }

    /// <summary>Whether this tile currently belongs to a terrain (DiVoid #7551 Phase 3).</summary>
    public bool IsTerrainVariant => Terrain.HasValue;

    /// <summary>
    /// This tile TYPE's default contact/contact-leave behavior binding (DiVoid #7738, design #7704 §6), or
    /// <c>null</c> when the type declares none. Carried through unchanged by every in-place tile edit below
    /// (mirrors <see cref="Terrain"/>/<see cref="PeeringBits"/>'s "unrelated edits don't drop it" treatment)
    /// so the editor's palette cache stays faithful to what the package actually authored — DiVoid #7747:
    /// before this field existed, <see cref="EditableLevelSnapshot"/> had no way to project a tile's behavior
    /// into a playtest's <c>ResolvedLevel</c> at all, so a scripted tile (e.g. the demo spike) silently did
    /// nothing when played from the editor's playtest overlay even though the same package plays correctly
    /// stand-alone. There is still no authoring UX for this (assigning/clearing a tile's default behavior
    /// remains P3 scope, per DiVoid #7738's own known-gap note) — this is read-through only.
    /// </summary>
    public ResolvedBehaviorBinding? Behavior { get; }
}
