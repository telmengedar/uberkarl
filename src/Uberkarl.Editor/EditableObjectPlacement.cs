using Uberkarl.Behavior;
using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>A placed object instance as the editor holds it: the authored placement verbatim, plus its type's merged render data.</summary>
public sealed class EditableObjectPlacement
{
    public EditableObjectPlacement(
        ObjectPlacement placement, ObjectCollisionRole collisionRole, byte[] graphic,
        BehaviorBinding? effectiveBehavior, IReadOnlyDictionary<string, object?> state)
    {
        Placement = placement ?? throw new ArgumentNullException(nameof(placement));
        CollisionRole = collisionRole;
        Graphic = graphic ?? throw new ArgumentNullException(nameof(graphic));
        EffectiveBehavior = effectiveBehavior;
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>The authored placement, unchanged — what a writer round-trips.</summary>
    public ObjectPlacement Placement { get; }

    /// <summary>The object type's collision body kind.</summary>
    public ObjectCollisionRole CollisionRole { get; }

    /// <summary>The object's graphic bytes.</summary>
    public byte[] Graphic { get; }

    /// <summary>This instance's resolved behavior (its own override, else the type default), or <c>null</c> for a decorative object.</summary>
    public BehaviorBinding? EffectiveBehavior { get; }

    /// <summary>The object type's default state, seeded into this instance's runtime state at spawn.</summary>
    public IReadOnlyDictionary<string, object?> State { get; }
}
