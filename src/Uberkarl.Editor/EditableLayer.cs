using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>
/// A mutable authoring view of one level layer. Unlike <see cref="LayerDefinition"/> (whose cells are
/// read-only) the grid here is a plain mutable array so edit commands can set individual cells in
/// place. Scroll/collision/repeat attributes are carried through unchanged for round-trip fidelity;
/// editing them is a later increment.
/// </summary>
public sealed class EditableLayer
{
    public EditableLayer(string name, bool collision, float scrollSpeed, bool repeat, int[] cells)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Collision = collision;
        ScrollSpeed = scrollSpeed;
        Repeat = repeat;
        Cells = cells ?? throw new ArgumentNullException(nameof(cells));
    }

    public string Name { get; }

    public bool Collision { get; }

    public float ScrollSpeed { get; }

    public bool Repeat { get; }

    /// <summary>Row-major grid of tile ids (<see cref="LayerDefinition.EmptyCell"/> for an empty cell). Mutated in place by edit commands.</summary>
    public int[] Cells { get; }
}
