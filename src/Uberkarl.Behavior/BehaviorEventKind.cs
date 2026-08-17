namespace Uberkarl.Behavior;

/// <summary>
/// The closed set of events a behavior script may react to (design #7704 §7). Which kinds are meaningful
/// depends on the subject (tile / object / area trigger / level) — a binding simply leaves the handlers for
/// events it doesn't care about unassigned; the scheduler treats an unassigned handler as a no-op.
/// </summary>
public enum BehaviorEventKind
{
    /// <summary>Object only: the instance becomes live at play start.</summary>
    OnSpawn,

    /// <summary>Tile / object: an edge-triggered contact begins (player or another object touches the subject).</summary>
    OnContact,

    /// <summary>Tile / object: an edge-triggered contact ends.</summary>
    OnContactLeave,

    /// <summary>Area trigger only: the player or an object enters the trigger's grid rect.</summary>
    OnEnter,

    /// <summary>Area trigger only: the player or an object leaves the trigger's grid rect.</summary>
    OnLeave,

    /// <summary>Object / level: runs once per frame with the elapsed seconds.</summary>
    OnUpdate,

    /// <summary>Level only: the level becomes active at play start.</summary>
    OnLevelStart,
}
