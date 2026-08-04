using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>
/// Sets one grid cell on one layer's LOGICAL terrain channel to a terrain id, remembering both the
/// previous terrain id and the previous concrete tile id so the edit reverses exactly (DiVoid #7551
/// Phase 3, design #7580 §7). Painting a terrain is <c>SetTerrainCommand(layer, x, y, terrainId)</c>;
/// erasing is the same command with <see cref="LayerDefinition.EmptyCell"/> — mirrors
/// <see cref="SetCellCommand"/>'s "one command, both tools" shape exactly.
///
/// <b>Two-channel invariant</b>: painting a terrain always clears that cell's concrete tile id too — a
/// cell is concrete XOR terrain-painted, never both. This command stores the LOGICAL paint only; the
/// actual matching-variant sprite is never computed or stored here — the caller (the Godot glue,
/// <c>LevelEditor.ReflowTerrain</c>) re-issues Godot's own terrain-connect resolution over the current
/// terrain-painted cells after every edit, so a neighbour's border re-flows without this command needing
/// to know anything about tiles, peering bits, or Godot at all.
/// </summary>
public sealed class SetTerrainCommand : IEditCommand
{
    private readonly int layerIndex;
    private readonly int x;
    private readonly int y;
    private readonly int newTerrainId;
    private int previousTerrainId;
    private int previousTileId;

    public SetTerrainCommand(int layerIndex, int x, int y, int newTerrainId)
    {
        this.layerIndex = layerIndex;
        this.x = x;
        this.y = y;
        this.newTerrainId = newTerrainId;
    }

    public CellChange Apply(EditableLevel level)
    {
        var index = Index(level);
        var layer = level.Layers[layerIndex];
        previousTerrainId = layer.Terrain[index];
        previousTileId = layer.Cells[index];
        layer.Terrain[index] = newTerrainId;
        layer.Cells[index] = LayerDefinition.EmptyCell;
        // The concrete channel is always empty after a terrain command — return that as the CellChange so
        // the controller can apply it via the SAME canvas.Apply(...) path a concrete edit uses, clearing
        // any stale concrete visual at this cell BEFORE the terrain-connect reflow repaints it (or leaves
        // it empty, for an erase). See LevelEditor.PaintTerrain/EraseTerrain.
        return new CellChange(layerIndex, x, y, LayerDefinition.EmptyCell);
    }

    public CellChange Revert(EditableLevel level)
    {
        var index = Index(level);
        var layer = level.Layers[layerIndex];
        layer.Terrain[index] = previousTerrainId;
        layer.Cells[index] = previousTileId;
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
