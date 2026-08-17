namespace Uberkarl.Behavior;

/// <summary>
/// Godot-free, intent-recording implementation of <see cref="ILevelFacade"/> — the reference facade bound
/// as <c>level</c> (see <see cref="BehaviorSubject"/> for the "one glue impl + one test double" rationale).
/// Owns the registry of named objects a script can reach via <see cref="Object"/>, a flat tile lookup, and
/// level-wide state — all host-seeded directly (<see cref="Objects"/>/<see cref="Tiles"/>/<see cref="State"/>
/// are plain public collections, not part of the script-facing contract).
/// </summary>
public sealed class BehaviorLevel : ILevelFacade
{
    private readonly IntentBuffer intents;

    public BehaviorLevel(IntentBuffer intents) => this.intents = intents;

    /// <summary>Host-seeded: objects a script can look up by instance name via <see cref="Object"/>/<see cref="ObjectsNamed"/>.</summary>
    public Dictionary<string, BehaviorSubject> Objects { get; } = new();

    /// <summary>Host-seeded: tile ids keyed by (layer, cell).</summary>
    public Dictionary<(int Layer, GridCell Cell), string> Tiles { get; } = new();

    /// <summary>Host-seeded: level-wide state map.</summary>
    public Dictionary<string, object?> State { get; } = new();

    public string? TileAt(int layer, GridCell cell) => Tiles.TryGetValue((layer, cell), out var tile) ? tile : null;

    public IObjectFacade? Object(string name) => Objects.TryGetValue(name, out var subject) ? subject : null;

    public IReadOnlyList<IObjectFacade> ObjectsNamed(string name) =>
        Objects.Values.Where(o => o.Name == name).Cast<IObjectFacade>().ToList();

    public object? GetState(string key) => State.TryGetValue(key, out var value) ? value : null;

    public void SetState(string key, object? value) =>
        intents.Record(new SetStateIntent(BehaviorSubjectIds.Level, key, value));
}
