using Uberkarl.Behavior;
using Uberkarl.Packages;

namespace Uberkarl.Content;

/// <summary>
/// One placed object instance stored on the level (DiVoid #7863, design #7704 §5.2/§6): a reference to its
/// <see cref="ObjectDefinition"/> (by owning <see cref="ObjectSet"/> resource + <see cref="ObjectId"/>), the
/// grid cell it spawns at, its instance name, and an optional per-instance behavior override. Grid-placed at
/// author time; free-moving at runtime (design #7704 §9.4) — <see cref="Cell"/> is only the spawn position.
/// </summary>
public sealed class ObjectPlacement
{
    /// <summary>The <c>objectset</c> resource declaring this placement's <see cref="ObjectId"/>.</summary>
    public required ResourceReference ObjectSet { get; init; }

    /// <summary>Which <see cref="ObjectDefinition.Id"/> within <see cref="ObjectSet"/> this instance is.</summary>
    public required string ObjectId { get; init; }

    /// <summary>The grid cell this instance spawns at.</summary>
    public GridPosition Cell { get; init; }

    /// <summary>Author-facing instance name. Not required to be unique — mirrors <see cref="AreaTriggerDefinition.Name"/>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Replaces this instance's <see cref="ObjectDefinition.Behavior"/> default when set. <c>null</c> uses the type default.</summary>
    public BehaviorBinding? Behavior { get; init; }
}
