using Uberkarl.Behavior;
using Uberkarl.Packages;

namespace Uberkarl.Content;

/// <summary>
/// One object TYPE declared in an <see cref="ObjectSetDefinition"/> (DiVoid #7863, design #7704 §5.2/§6,
/// mirrors <see cref="TileDefinition"/>'s type-level-defaults role): a graphic, a collision role, an
/// optional default behavior binding, and an optional default state map. Placed on a level via
/// <see cref="ObjectPlacement"/>, which references this by <see cref="Id"/>.
/// </summary>
public sealed class ObjectDefinition
{
    /// <summary>Stable id, unique within the owning <see cref="ObjectSetDefinition"/>. Referenced by <see cref="ObjectPlacement.ObjectId"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Optional author-facing display name. Purely cosmetic.</summary>
    public string? Name { get; init; }

    /// <summary>The object's graphic, drawn at its live position (tile-sized) regardless of grid alignment.</summary>
    public ResourceReference Graphic { get; init; }

    /// <summary>Which Godot body type this object's placements spawn as (DiVoid #7863, design #7704 §9.4).</summary>
    public ObjectCollisionRole CollisionRole { get; init; } = ObjectCollisionRole.Solid;

    /// <summary>
    /// This object TYPE's default behavior binding, applied to every placement unless overridden per
    /// instance by <see cref="ObjectPlacement.Behavior"/>. <c>null</c> for a purely decorative object.
    /// </summary>
    public BehaviorBinding? Behavior { get; init; }

    /// <summary>
    /// This object TYPE's default initial state map (design #7704 §15 Q-1 — free-form for MVP), seeded into
    /// every placement's runtime state at spawn. Empty for an object with no state.
    /// </summary>
    public IReadOnlyDictionary<string, object?> State { get; init; } = new Dictionary<string, object?>();
}
