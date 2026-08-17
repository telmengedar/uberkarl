namespace Uberkarl.Behavior;

/// <summary>
/// The facade bound as <c>level</c> (design #7704 §8.1, "level" row). The gateway to querying tiles, other
/// objects, and level-wide state. Reads return values or other narrow <see cref="IObjectFacade"/> facades —
/// never a live engine collection.
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

    /// <summary>Records an intent setting a value in the level's state map.</summary>
    void SetState(string key, object? value);
}
