using Uberkarl.Behavior;

namespace Uberkarl.Editor;

/// <summary>
/// The scriptable subject (if any) a grid cell resolves to for behavior assignment (design #8049 M4): an
/// object occupying the cell, else a trigger whose rect contains it, else the tile instance at the cell on
/// the active layer. Pure query result — see <see cref="EditableLevel.FindBehaviorSubjectAt"/>. The level
/// script is not cell-addressed and has no corresponding factory here.
/// </summary>
public readonly struct BehaviorSubjectTarget
{
    private BehaviorSubjectTarget(bool found, BehaviorSubjectKind kind, int index, int layer, int x, int y)
    {
        Found = found;
        Kind = kind;
        Index = index;
        Layer = layer;
        X = x;
        Y = y;
    }

    /// <summary>Whether a subject was found at the queried cell.</summary>
    public bool Found { get; }

    /// <summary>The found subject's kind. Meaningless when <see cref="Found"/> is <c>false</c>.</summary>
    public BehaviorSubjectKind Kind { get; }

    /// <summary>The object/trigger's index into <see cref="EditableLevel.Objects"/>/<see cref="EditableLevel.Triggers"/>. <c>-1</c> for a tile target.</summary>
    public int Index { get; }

    /// <summary>The tile's layer index. <c>-1</c> for an object/trigger target.</summary>
    public int Layer { get; }

    /// <summary>The tile's cell X. <c>-1</c> for an object/trigger target.</summary>
    public int X { get; }

    /// <summary>The tile's cell Y. <c>-1</c> for an object/trigger target.</summary>
    public int Y { get; }

    /// <summary>No subject at the queried cell.</summary>
    public static BehaviorSubjectTarget None { get; } = new(false, default, -1, -1, -1, -1);

    /// <summary>The placed object at <see cref="EditableLevel.Objects"/> index <paramref name="index"/>.</summary>
    public static BehaviorSubjectTarget ForObject(int index) => new(true, BehaviorSubjectKind.Object, index, -1, -1, -1);

    /// <summary>The trigger at <see cref="EditableLevel.Triggers"/> index <paramref name="index"/>.</summary>
    public static BehaviorSubjectTarget ForTrigger(int index) => new(true, BehaviorSubjectKind.Trigger, index, -1, -1, -1);

    /// <summary>The tile instance at (<paramref name="layer"/>, <paramref name="x"/>, <paramref name="y"/>).</summary>
    public static BehaviorSubjectTarget ForTile(int layer, int x, int y) => new(true, BehaviorSubjectKind.Tile, -1, layer, x, y);

    /// <summary>The level script — not cell-addressed, reached through a menu rather than <see cref="EditableLevel.FindBehaviorSubjectAt"/>.</summary>
    public static BehaviorSubjectTarget ForLevelScript() => new(true, BehaviorSubjectKind.LevelScript, -1, -1, -1, -1);
}
