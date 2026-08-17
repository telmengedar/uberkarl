namespace Uberkarl.Behavior;

/// <summary>Raised exactly once per subject the moment it is quarantined.</summary>
public sealed record BehaviorQuarantineEvent(string SubjectId, BehaviorEventKind? TriggeringEvent, string Reason);

/// <summary>Owns the registry of live <see cref="BehaviorInstance"/>s and routes events to their cached handlers, quarantining a subject on breach or exception.</summary>
public sealed class BehaviorScheduler
{
    private readonly Dictionary<string, BehaviorInstance> instances = new();

    /// <summary>
    /// The shared <c>event</c> facade object (design #7704 §8.1). Bind this as the "event" global
    /// alongside self/level/player when compiling every subject's behavior — the scheduler updates its
    /// fields immediately before each dispatch.
    /// </summary>
    public BehaviorEvent CurrentEvent { get; } = new();

    /// <summary>Fires exactly once per subject when it is quarantined, whether at registration (already-quarantined from <see cref="BehaviorLoader.Compile"/>) or on a later runtime breach.</summary>
    public event Action<BehaviorQuarantineEvent>? Quarantined;

    public void Register(BehaviorInstance instance)
    {
        instances[instance.SubjectId] = instance;

        if (instance.IsQuarantined)
            Quarantined?.Invoke(new BehaviorQuarantineEvent(instance.SubjectId, null, instance.Compiled.QuarantineReason ?? "quarantined before registration"));
    }

    public bool IsQuarantined(string subjectId) => instances.TryGetValue(subjectId, out var instance) && instance.IsQuarantined;

    public IReadOnlyCollection<string> RegisteredSubjectIds => instances.Keys;

    /// <summary>Dispatches one event to one subject's cached handler. Prefer the typed <c>Dispatch*</c> helpers below, which also keep <see cref="CurrentEvent"/> in sync.</summary>
    public bool Dispatch(string subjectId, BehaviorEventKind kind, params object?[] arguments)
    {
        if (!instances.TryGetValue(subjectId, out var instance) || instance.IsQuarantined)
            return false;

        if (!instance.Compiled.Handlers.TryGetValue(kind, out var handler))
            return false;

        if (ScriptExecutionGuard.TryRun(() => handler.Invoke(arguments), out _, out var failureReason))
            return true;

        var reason = $"{kind} {failureReason}";
        instance.Compiled.Quarantine(reason);
        Quarantined?.Invoke(new BehaviorQuarantineEvent(subjectId, kind, reason));
        return false;
    }

    public bool DispatchSpawn(string subjectId)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnSpawn);
        return Dispatch(subjectId, BehaviorEventKind.OnSpawn);
    }

    public bool DispatchContact(string subjectId, EventParty other)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnContact);
        CurrentEvent.Other = other;
        return Dispatch(subjectId, BehaviorEventKind.OnContact, other);
    }

    public bool DispatchContactLeave(string subjectId, EventParty other)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnContactLeave);
        CurrentEvent.Other = other;
        return Dispatch(subjectId, BehaviorEventKind.OnContactLeave, other);
    }

    public bool DispatchEnter(string subjectId, EventParty who)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnEnter);
        CurrentEvent.Other = who;
        return Dispatch(subjectId, BehaviorEventKind.OnEnter, who);
    }

    public bool DispatchLeave(string subjectId, EventParty who)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnLeave);
        CurrentEvent.Other = who;
        return Dispatch(subjectId, BehaviorEventKind.OnLeave, who);
    }

    public bool DispatchUpdate(string subjectId, double deltaSeconds)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnUpdate);
        CurrentEvent.Delta = deltaSeconds;
        return Dispatch(subjectId, BehaviorEventKind.OnUpdate, deltaSeconds);
    }

    public bool DispatchLevelStart(string subjectId)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnLevelStart);
        return Dispatch(subjectId, BehaviorEventKind.OnLevelStart);
    }
}
