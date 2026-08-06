using Uberkarl.Behavior;

namespace Uberkarl.Content;

/// <summary>
/// A grid-rect area trigger stored on the level (DiVoid #7738, design #7704 §6 — "grid rect (x,y,w,h cells) +
/// binding + name, stored on the level"). Fires <c>onEnter</c>/<c>onLeave</c> on its bound behavior when the
/// player (P1) or an object (P2, not yet placeable) crosses the rect's boundary.
/// </summary>
public sealed class AreaTriggerDefinition
{
    /// <summary>
    /// Author-facing instance name (design #7704 §6). Not required to be unique — <c>LevelLoader</c> does not
    /// enforce uniqueness, mirroring <c>Uberkarl.Behavior.ILevelFacade.ObjectsNamed</c>'s "design allows
    /// non-unique names for e.g. patrol groups".
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Rect left edge, in grid (tile) units.</summary>
    public int X { get; init; }

    /// <summary>Rect top edge, in grid (tile) units.</summary>
    public int Y { get; init; }

    /// <summary>Rect width, in grid (tile) units. Must be positive.</summary>
    public int Width { get; init; }

    /// <summary>Rect height, in grid (tile) units. Must be positive.</summary>
    public int Height { get; init; }

    /// <summary>The trigger's <c>onEnter</c>/<c>onLeave</c> behavior binding. Required — a trigger with no binding is pointless content the loader rejects.</summary>
    public required BehaviorBinding Binding { get; init; }
}
