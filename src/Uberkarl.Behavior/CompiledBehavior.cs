namespace Uberkarl.Behavior;

using System.Threading;

/// <summary>
/// The reusable result of parsing a behavior script's Pooscript text once and running its one-time init
/// execute to discover handlers (design #7704 §5.3/§7 — "parse + init once, cheap invoke per event").
/// Produced by <see cref="BehaviorLoader.Compile"/>, held by a <see cref="BehaviorInstance"/>, dispatched
/// through by <see cref="BehaviorScheduler"/>.
/// </summary>
public sealed class CompiledBehavior
{
    internal CompiledBehavior(IReadOnlyDictionary<BehaviorEventKind, BehaviorHandler> handlers, CancellationTokenSource cancellation)
    {
        Handlers = handlers;
        Cancellation = cancellation;
    }

    /// <summary>Handlers discovered at init, keyed by the event they were assigned to (e.g. <c>$onContact = ...</c>). Missing entries are simply not implemented by this script.</summary>
    public IReadOnlyDictionary<BehaviorEventKind, BehaviorHandler> Handlers { get; }

    /// <summary>True once this behavior has been quarantined (init failure, or a later runtime budget breach/exception) — the scheduler skips dispatch entirely once true.</summary>
    public bool IsQuarantined { get; private set; }

    /// <summary>Human-readable reason for the quarantine, set exactly once (design #7704 §8.3 — "logged once, not per frame").</summary>
    public string? QuarantineReason { get; private set; }

    /// <summary>This instance's own cancellation source (design #7704 §8.3/§8.4) — shared by the init execute and every subsequent handler invocation, since they all resolve <c>self</c>/<c>level</c>/<c>player</c>/<c>event</c> through the same captured script context. Cancelled on quarantine as a best-effort cooperative signal.</summary>
    internal CancellationTokenSource Cancellation { get; }

    public bool HasHandler(BehaviorEventKind kind) => !IsQuarantined && Handlers.ContainsKey(kind);

    /// <summary>Marks this behavior quarantined. Idempotent: only the first call records a reason and cancels the token; later calls (e.g. a second breach before the scheduler notices the first) are no-ops.</summary>
    internal void Quarantine(string reason)
    {
        if (IsQuarantined)
            return;

        IsQuarantined = true;
        QuarantineReason = reason;
        Cancellation.Cancel();
    }
}
