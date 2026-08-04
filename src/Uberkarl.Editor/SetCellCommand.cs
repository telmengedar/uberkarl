using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>
/// Sets one grid cell on one layer to a tile id, remembering the previous id so the edit reverses
/// exactly. Painting is <c>SetCellCommand(layer, x, y, tileId)</c>; erasing is the same command with
/// <see cref="LayerDefinition.EmptyCell"/> — one command covers both tools.
///
/// <b>Two-channel invariant</b> (DiVoid #7551 Phase 3, design #7580 §7): placing a concrete tile always
/// clears that cell's terrain paint too — a cell is concrete XOR terrain-painted, never both. The cleared
/// terrain id is remembered so <see cref="Revert"/> restores it exactly, mirroring <see cref="SetTerrainCommand"/>.
/// </summary>
public sealed class SetCellCommand : IEditCommand
{
    private readonly int layerIndex;
    private readonly int x;
    private readonly int y;
    private readonly int newTileId;
    private int previousTileId;
    private int previousTerrainId;

    public SetCellCommand(int layerIndex, int x, int y, int newTileId)
    {
        this.layerIndex = layerIndex;
        this.x = x;
        this.y = y;
        this.newTileId = newTileId;
    }

    public CellChange Apply(EditableLevel level)
    {
        var index = Index(level);
        var layer = level.Layers[layerIndex];
        previousTileId = layer.Cells[index];
        previousTerrainId = layer.Terrain[index];
        layer.Cells[index] = newTileId;
        layer.Terrain[index] = LayerDefinition.EmptyCell;
        return new CellChange(layerIndex, x, y, newTileId);
    }

    public CellChange Revert(EditableLevel level)
    {
        var index = Index(level);
        var layer = level.Layers[layerIndex];
        layer.Cells[index] = previousTileId;
        layer.Terrain[index] = previousTerrainId;
        return new CellChange(layerIndex, x, y, previousTileId);
    }

    private int Index(EditableLevel level)
    {
        if (layerIndex < 0 || layerIndex >= level.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        var index = level.CellIndex(x, y);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x},{y}) is outside the {level.Width}x{level.Height} grid.");
        return index;
    }
}
