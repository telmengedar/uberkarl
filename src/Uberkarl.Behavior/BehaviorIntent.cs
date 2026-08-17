namespace Uberkarl.Behavior;

/// <summary>
/// Well-known target ids for intents that address a singleton subject rather than a named tile/object/
/// trigger instance.
/// </summary>
public static class BehaviorSubjectIds
{
    public const string Level = "level";

    public const string Player = "player";
}

/// <summary>
/// The closed set of mutation intents a script can request (design #7704 §5.6/§8.5 — the single-thread
/// mutation contract). Handlers never mutate anything directly: every facade action call records one of
/// these instead. The host applies them on the main thread, after the whole behavior phase, in
/// <see cref="IntentBuffer"/> order — the intent buffer IS the read-snapshot/write-intent contract that
/// makes the behavior phase deterministic and race-free.
/// </summary>
/// <param name="SubjectId">Which subject this intent applies to (design #7704 §6 "Runtime entities" — a <see cref="BehaviorSubjectIds"/> constant for level/player, or a subject's own <see cref="ISelfFacade.Id"/> otherwise).</param>
public abstract record BehaviorIntent(string SubjectId);

/// <summary>Move the subject to an absolute grid cell.</summary>
public sealed record MoveToCellIntent(string SubjectId, GridCell Cell) : BehaviorIntent(SubjectId);

/// <summary>Move the subject to an absolute continuous position.</summary>
public sealed record MoveToPositionIntent(string SubjectId, BehaviorVector2 Position) : BehaviorIntent(SubjectId);

/// <summary>Move the subject by a relative delta.</summary>
public sealed record MoveByIntent(string SubjectId, double Dx, double Dy) : BehaviorIntent(SubjectId);

/// <summary>Set a key in the subject's state map.</summary>
public sealed record SetStateIntent(string SubjectId, string Key, object? Value) : BehaviorIntent(SubjectId);

/// <summary>Damage the player.</summary>
public sealed record HurtIntent(string SubjectId, double Amount) : BehaviorIntent(SubjectId);

/// <summary>Heal the player.</summary>
public sealed record HealIntent(string SubjectId, double Amount) : BehaviorIntent(SubjectId);

/// <summary>
/// Collects intents recorded by facade objects during a behavior phase, in issuance order (design #7704
/// §7 step 4 — "Scheduler returns the collected intents; glue applies them on the main thread"). One buffer
/// is shared by every facade object bound into a phase's scripts; the host drains it once the phase (or
/// frame) completes.
/// </summary>
public sealed class IntentBuffer
{
    private readonly List<BehaviorIntent> intents = new();

    public IReadOnlyList<BehaviorIntent> Intents => intents;

    public void Record(BehaviorIntent intent) => intents.Add(intent);

    /// <summary>Returns and clears the collected intents — the host calls this once per frame/phase after every dispatch has run.</summary>
    public IReadOnlyList<BehaviorIntent> Drain()
    {
        if (intents.Count == 0)
            return Array.Empty<BehaviorIntent>();

        var drained = intents.ToArray();
        intents.Clear();
        return drained;
    }
}
