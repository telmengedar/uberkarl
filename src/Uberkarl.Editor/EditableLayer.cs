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
    public EditableLayer(string name, bool collision, float scrollSpeed, bool repeat, int[] cells, int[]? terrain = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Collision = collision;
        ScrollSpeed = scrollSpeed;
        Repeat = repeat;
        Cells = cells ?? throw new ArgumentNullException(nameof(cells));

        // DiVoid #7551 Phase 3: the parallel logical-terrain-paint channel (design #7580 §7). Defaults to an
        // all-empty array sized to match Cells so every existing/omitted-terrain layer round-trips as "no
        // terrain painted" without every call site having to construct one.
        if (terrain is not null && terrain.Length != cells.Length)
            throw new ArgumentException($"Terrain channel has {terrain.Length} entries but Cells has {cells.Length}.", nameof(terrain));
        Terrain = terrain ?? CreateEmptyTerrain(cells.Length);
    }

    private static int[] CreateEmptyTerrain(int length)
    {
        var terrain = new int[length];
        Array.Fill(terrain, Content.LayerDefinition.EmptyCell);
        return terrain;
    }

    public string Name { get; }

    public bool Collision { get; }

    public float ScrollSpeed { get; }

    public bool Repeat { get; }

    /// <summary>Row-major grid of tile ids (<see cref="LayerDefinition.EmptyCell"/> for an empty cell). Mutated in place by edit commands.</summary>
    public int[] Cells { get; }

    /// <summary>
    /// Row-major grid of terrain ids, parallel to <see cref="Cells"/> (<see cref="LayerDefinition.EmptyCell"/>
    /// where not terrain-painted) — DiVoid #7551 Phase 3, design #7580 §7. Mutated in place by edit commands;
    /// a cell is concrete XOR terrain-painted (enforced by <see cref="SetCellCommand"/>/<see cref="SetTerrainCommand"/>,
    /// which always clear the other channel).
    /// </summary>
    public int[] Terrain { get; }
}
