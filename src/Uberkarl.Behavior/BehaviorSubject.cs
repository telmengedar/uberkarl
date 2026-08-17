namespace Uberkarl.Behavior;

/// <summary>
/// Godot-free, intent-recording implementation of <see cref="ISelfFacade"/> and <see cref="IObjectFacade"/> —
/// one instance represents one live scripted subject (a tile instance, a free-moving object, an area
/// trigger, or the level itself when bound as <c>self</c>), or another object as seen through
/// <c>level.object(name)</c>. This is the reference facade design #7704 §5.8 calls for as "one glue impl +
/// one test double": Godot-free by construction, it doubles as the test double for unit tests today and the
/// base every future Godot glue composes with (a glue impl only needs to keep this snapshot's fields synced
/// with the real engine node — it never needs to reimplement the facade contract).
/// Reads return the subject's own current field values; per the empirically-verified closure behavior of
/// Pooshit.Scripting (a cached lambda re-reads a bound host object's members live on each invocation), the
/// host/scheduler updates these fields before each dispatch and already-compiled handlers see the update —
/// no re-parsing needed. Actions never mutate these fields directly; they only record an intent.
/// </summary>
public sealed class BehaviorSubject : ISelfFacade, IObjectFacade
{
    private readonly IntentBuffer intents;
    private readonly Dictionary<string, object?> state = new();

    public BehaviorSubject(string id, string kind, string name, IntentBuffer intents)
    {
        Id = id;
        Kind = kind;
        Name = name;
        this.intents = intents;
    }

    public string Id { get; }

    public string Kind { get; }

    public string Name { get; set; }

    public GridCell Cell { get; set; }

    public BehaviorVector2 Position { get; set; }

    /// <summary>Host-side direct write, bypassing the intent buffer — used to seed initial state and to apply drained intents; never called from script code.</summary>
    public void SeedState(string key, object? value) => state[key] = value;

    public object? GetState(string key) => state.TryGetValue(key, out var value) ? value : null;

    public void MoveTo(GridCell cell) => intents.Record(new MoveToCellIntent(Id, cell));

    public void MoveTo(BehaviorVector2 position) => intents.Record(new MoveToPositionIntent(Id, position));

    public void MoveBy(double dx, double dy) => intents.Record(new MoveByIntent(Id, dx, dy));

    public void SetState(string key, object? value) => intents.Record(new SetStateIntent(Id, key, value));
}
