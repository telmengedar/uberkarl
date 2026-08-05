using Uberkarl.Behavior;

namespace Uberkarl.Content;

/// <summary>
/// One entry of the level's sparse <c>(layer,cell) → binding | removed</c> tile-behavior override map
/// (DiVoid #7738, design #7704 §6 — "instance-level wiring and deltas... live on the level"). Expresses the
/// vision's "default script overridable/removable per instance": a level either replaces the tileset-default
/// binding for one placed tile instance with a different one (<see cref="Binding"/> set), or explicitly
/// silences it (<see cref="Removed"/> true, e.g. "this one spike is inert") even though the tile TYPE still
/// declares a default. Exactly one of <see cref="Binding"/> / <see cref="Removed"/> is meaningful —
/// <c>LevelLoader</c> validates the pair is never both set and never neither.
/// </summary>
public sealed class TileBehaviorOverride
{
    /// <summary>Index into <see cref="LevelDefinition.Layers"/> the overridden cell belongs to.</summary>
    public int Layer { get; init; }

    /// <summary>The overridden cell, in grid (tile) units.</summary>
    public GridPosition Cell { get; init; }

    /// <summary>The replacement binding for this instance. Null when <see cref="Removed"/> is true.</summary>
    public BehaviorBinding? Binding { get; init; }

    /// <summary>
    /// True when this instance explicitly has NO behavior, overriding the tile type's default even though
    /// the type declares one. Mutually exclusive with <see cref="Binding"/>.
    /// </summary>
    public bool Removed { get; init; }
}
