namespace Uberkarl.Behavior;

/// <summary>
/// A small, read-only descriptor of "who" is involved in a contact / enter / leave event (design #7704
/// §8.2 — "<c>event.other</c> is a small read-only descriptor, not the <c>CharacterBody2D</c>"). Never a
/// mutable facade: to act on the other party a script goes through <see cref="ILevelFacade.Object"/> or
/// <see cref="IPlayerFacade"/> explicitly.
/// </summary>
/// <param name="Kind">"player", "object", or "tile" — which kind of subject this describes.</param>
/// <param name="Name">The subject's instance name (empty for the player / anonymous subjects).</param>
/// <param name="Cell">The subject's current cell.</param>
public sealed record EventParty(string Kind, string Name, GridCell Cell);

/// <summary>
/// The <c>event</c> facade bound object (design #7704 §8.1 event row). One instance is owned by the
/// <see cref="BehaviorScheduler"/> and reused across every dispatch — handlers observe it live because
/// Pooscript closures resolve host object member reads at invocation time (verified empirically: a facade
/// object's fields mutated between invocations are visible on the next call to an already-cached handler),
/// so the scheduler simply updates these fields immediately before invoking a handler for this event.
/// </summary>
public sealed class BehaviorEvent
{
    /// <summary>The event kind's canonical handler name (e.g. "onContact"), for scripts that want to branch on it generically.</summary>
    public string Kind { get; internal set; } = string.Empty;

    /// <summary>Contact / enter / leave: who the subject touched or who entered/left. Null otherwise.</summary>
    public EventParty? Other { get; internal set; }

    /// <summary><see cref="BehaviorEventKind.OnUpdate"/>: elapsed seconds since the last frame.</summary>
    public double Delta { get; internal set; }

    internal void Reset(BehaviorEventKind kind)
    {
        Kind = BehaviorEventNames.ToVariableName(kind);
        Other = null;
        Delta = 0;
    }
}
