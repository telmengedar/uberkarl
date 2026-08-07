namespace Uberkarl.Behavior;

/// <summary>The reusable result of parsing a behavior script once and running its one-time init execute to discover handlers.</summary>
public sealed class CompiledBehavior
{
    internal CompiledBehavior(IReadOnlyDictionary<BehaviorEventKind, BehaviorHandler> handlers) => Handlers = handlers;

    /// <summary>Handlers discovered at init, keyed by the event they were assigned to (e.g. <c>$onContact = ...</c>).</summary>
    public IReadOnlyDictionary<BehaviorEventKind, BehaviorHandler> Handlers { get; }

    /// <summary>True once this behavior has been quarantined.</summary>
    public bool IsQuarantined { get; private set; }

    /// <summary>Human-readable reason for the quarantine, set exactly once.</summary>
    public string? QuarantineReason { get; private set; }

    public bool HasHandler(BehaviorEventKind kind) => !IsQuarantined && Handlers.ContainsKey(kind);

    /// <summary>Marks this behavior quarantined. Idempotent: only the first call records a reason.</summary>
    internal void Quarantine(string reason)
    {
        if (IsQuarantined)
            return;

        IsQuarantined = true;
        QuarantineReason = reason;
    }
}
