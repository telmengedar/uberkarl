using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>
/// Sets one grid cell on one layer to a tile id, remembering the previous id so the edit reverses
/// exactly. Painting is <c>SetCellCommand(layer, x, y, tileId)</c>; erasing is the same command with
/// <see cref="LayerDefinition.EmptyCell"/> — one command covers both tools.
/// </summary>
public sealed class SetCellCommand : IEditCommand
{
    private readonly int layerIndex;
    private readonly int x;
    private readonly int y;
    private readonly int newTileId;
    private int previousTileId;

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
        previousTileId = level.Layers[layerIndex].Cells[index];
        level.Layers[layerIndex].Cells[index] = newTileId;
        return new CellChange(layerIndex, x, y, newTileId);
    }

    public CellChange Revert(EditableLevel level)
    {
        var index = Index(level);
        level.Layers[layerIndex].Cells[index] = previousTileId;
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
