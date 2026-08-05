namespace Uberkarl.Behavior;

/// <summary>
/// The facade a script sees when it reaches ANOTHER object via <c>level.object(name)</c> (design #7704
/// §8.1, "another object" row) — narrower than <see cref="ISelfFacade"/> by design: a script may move,
/// restate, or message another object, but not despawn it, change its graphic, or schedule its timers.
/// Narrow and purpose-built per the capability boundary (C-8/§8.2): never a live engine node, only value
/// reads and recorded intents.
/// </summary>
public interface IObjectFacade
{
    /// <summary>The instance name this object was placed/spawned with.</summary>
    string Name { get; }

    /// <summary>Current grid cell (design #7704 §9.4 — objects are free-moving at runtime; cell is a derived/nearest-cell read for authoring convenience).</summary>
    GridCell Cell { get; }

    /// <summary>Reads a value from this object's state map, or null if unset.</summary>
    object? GetState(string key);

    /// <summary>Records an intent moving this object to a grid cell.</summary>
    void MoveTo(GridCell cell);

    /// <summary>Records an intent moving this object to a continuous world position.</summary>
    void MoveTo(BehaviorVector2 position);

    /// <summary>Records an intent setting a value in this object's state map.</summary>
    void SetState(string key, object? value);

    /// <summary>Records an intent sending a message to this object.</summary>
    void Message(string name, object? data);
}
