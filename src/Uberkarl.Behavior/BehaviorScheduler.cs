namespace Uberkarl.Behavior;

/// <summary>Raised exactly once per subject the moment it is quarantined — either at registration (already quarantined by <see cref="BehaviorLoader"/>) or on its first runtime budget breach/exception (design #7704 §8.3 — "logged once, not per frame").</summary>
public sealed record BehaviorQuarantineEvent(string SubjectId, BehaviorEventKind? TriggeringEvent, string Reason);

/// <summary>
/// Owns the registry of live <see cref="BehaviorInstance"/>s and routes events to their cached handlers
/// (design #7704 §5.4). Every dispatch runs through the shared <see cref="BehaviorWatchdog"/>; a breach or
/// exception quarantines that instance permanently (dispatch becomes a silent no-op from then on) and raises
/// <see cref="Quarantined"/> exactly once — the scheduler itself is never blocked past the watchdog's
/// budget, and a quarantined subject never gets dispatched to again, so the "logged once" guarantee falls
/// out of the registration/quarantine state machine rather than needing separate bookkeeping.
///
/// <para>
/// Does NOT own producing raw engine events (a glue layer pushes those in via the <c>Dispatch*</c> calls)
/// nor applying the intents handlers record (the host drains <see cref="IntentBuffer"/> after the phase) —
/// per design #7704 §5.4's explicit non-ownership.
/// </para>
/// </summary>
public sealed class BehaviorScheduler
{
    private readonly BehaviorWatchdog watchdog;
    private readonly Dictionary<string, BehaviorInstance> instances = new();

    public BehaviorScheduler(BehaviorWatchdog watchdog) => this.watchdog = watchdog;

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

    /// <summary>
    /// Dispatches one event to one subject's cached handler, watchdog-guarded. Returns false (a silent
    /// no-op) when the subject isn't registered, is already quarantined, or has no handler for this event
    /// kind; returns false and quarantines the subject when the handler breaches budget or throws. Prefer
    /// the typed <c>Dispatch*</c> helpers below, which also keep <see cref="CurrentEvent"/> in sync.
    /// </summary>
    public bool Dispatch(string subjectId, BehaviorEventKind kind, params object?[] arguments)
    {
        if (!instances.TryGetValue(subjectId, out var instance) || instance.IsQuarantined)
            return false;

        if (!instance.Compiled.Handlers.TryGetValue(kind, out var handler))
            return false;

        var outcome = watchdog.Execute(() => handler.Invoke(arguments), instance.Compiled.Cancellation);
        if (outcome.Kind == BehaviorWatchdogOutcomeKind.Completed)
            return true;

        var reason = outcome.Kind == BehaviorWatchdogOutcomeKind.BudgetExceeded
            ? $"{kind} exceeded the {watchdog.Budget.TotalMilliseconds}ms watchdog budget"
            : $"{kind} threw: {outcome.Exception}";
        instance.Compiled.Quarantine(reason);
        Quarantined?.Invoke(new BehaviorQuarantineEvent(subjectId, kind, reason));
        return false;
    }

    public bool DispatchSpawn(string subjectId)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnSpawn);
        return Dispatch(subjectId, BehaviorEventKind.OnSpawn);
    }

    public bool DispatchDespawn(string subjectId)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnDespawn);
        return Dispatch(subjectId, BehaviorEventKind.OnDespawn);
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

    public bool DispatchMessage(string subjectId, string name, object? data)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnMessage);
        CurrentEvent.MessageName = name;
        CurrentEvent.MessagePayload = data;
        return Dispatch(subjectId, BehaviorEventKind.OnMessage, name, data);
    }

    public bool DispatchLevelStart(string subjectId)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnLevelStart);
        return Dispatch(subjectId, BehaviorEventKind.OnLevelStart);
    }

    public bool DispatchPlayerDeath(string subjectId)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnPlayerDeath);
        return Dispatch(subjectId, BehaviorEventKind.OnPlayerDeath);
    }

    public bool DispatchPlayerRespawn(string subjectId)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnPlayerRespawn);
        return Dispatch(subjectId, BehaviorEventKind.OnPlayerRespawn);
    }

    public bool DispatchTimer(string subjectId, string tag)
    {
        CurrentEvent.Reset(BehaviorEventKind.OnTimer);
        CurrentEvent.Tag = tag;
        return Dispatch(subjectId, BehaviorEventKind.OnTimer, tag);
    }
}
