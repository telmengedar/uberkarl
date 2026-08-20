namespace Uberkarl.Behavior;

/// <summary>Which of the four scriptable subjects a predefined behavior may be bound to.</summary>
public enum BehaviorSubjectKind
{
    /// <summary>A placed tile instance (a tile-type default, or a per-instance override).</summary>
    Tile,

    /// <summary>A grid-rect area trigger.</summary>
    Trigger,

    /// <summary>A placed free-moving object.</summary>
    Object,

    /// <summary>The level script.</summary>
    LevelScript,
}
