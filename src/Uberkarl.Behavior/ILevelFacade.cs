namespace Uberkarl.Behavior;

/// <summary>
/// The facade bound as <c>level</c> (design #7704 §8.1, "level" row). The gateway to querying tiles, other
/// objects, and level-wide state, and to spawning/mutating structure. Reads return values or other narrow
/// <see cref="IObjectFacade"/> facades — never a live engine collection.
/// </summary>
public interface ILevelFacade
{
    /// <summary>The tile id at a cell on a given layer, or null if empty/out of bounds.</summary>
    string? TileAt(int layer, GridCell cell);

    /// <summary>The single object with the given instance name, or null if none.</summary>
    IObjectFacade? Object(string name);

    /// <summary>All objects sharing the given instance name (design allows non-unique names for e.g. patrol groups).</summary>
    IReadOnlyList<IObjectFacade> ObjectsNamed(string name);

    /// <summary>Reads a value from the level's state map, or null if unset.</summary>
    object? GetState(string key);

    /// <summary>Records an intent spawning a new object instance from an object-definition reference at a cell.</summary>
    void Spawn(string objectDefinitionRef, GridCell cell);

    /// <summary>Records an intent changing a tile at a cell (design #7704 §15 Q-3 — structural mutation, bounded scope deferred to Phase 1+).</summary>
    void SetTile(int layer, GridCell cell, string tileId);

    /// <summary>Records an intent setting a value in the level's state map.</summary>
    void SetState(string key, object? value);

    /// <summary>Records an intent sending a named message to a target subject.</summary>
    void Message(string target, string name, object? data);
}
