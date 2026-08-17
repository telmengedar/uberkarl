namespace Uberkarl.Behavior;

/// <summary>
/// The facade bound as <c>self</c> — the running entity itself (design #7704 §8.1, "self" row). Not
/// <see cref="IObjectFacade"/>-derived: self's capability set is a sibling, not a superset. Every action
/// records a <see cref="BehaviorIntent"/>; nothing here mutates state directly (design #7704 §8.5, the
/// single-thread mutation contract).
/// </summary>
public interface ISelfFacade
{
    /// <summary>Stable id of the underlying subject (tile-instance, object, trigger, or level) — the target key intents carry.</summary>
    string Id { get; }

    /// <summary>The subject kind: "tile", "object", "trigger", or "level".</summary>
    string Kind { get; }

    /// <summary>The instance name (empty for tiles/level/anonymous triggers).</summary>
    string Name { get; }

    /// <summary>Current grid cell.</summary>
    GridCell Cell { get; }

    /// <summary>Current continuous world position (design #7704 §9.4 — free-moving at runtime).</summary>
    BehaviorVector2 Position { get; }

    /// <summary>Reads a value from this subject's state map, or null if unset.</summary>
    object? GetState(string key);

    /// <summary>Records an intent moving this subject to a grid cell.</summary>
    void MoveTo(GridCell cell);

    /// <summary>Records an intent moving this subject to a continuous world position.</summary>
    void MoveTo(BehaviorVector2 position);

    /// <summary>Records an intent moving this subject by a relative delta.</summary>
    void MoveBy(double dx, double dy);

    /// <summary>Records an intent setting a value in this subject's state map.</summary>
    void SetState(string key, object? value);
}
