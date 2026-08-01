using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>
/// The façade the editor UI drives. It owns the <see cref="EditableLevel"/> (the model) and its
/// <see cref="EditHistory"/>, and exposes the whole edit surface as intent-level calls — paint, erase,
/// undo, redo, save — each returning the single <see cref="CellChange"/> the canvas must reflect (or
/// <c>null</c> for a no-op). The UI never mutates the model directly: it calls the session, then
/// applies the returned change to the canvas. This keeps the model authoritative and every mutation
/// on the undoable command path.
/// </summary>
public sealed class LevelEditSession
{
    private readonly EditHistory history = new();

    public LevelEditSession(EditableLevel level)
    {
        Level = level ?? throw new ArgumentNullException(nameof(level));
    }

    /// <summary>The level under edit.</summary>
    public EditableLevel Level { get; }

    /// <summary>True when there are unsaved edits (any applied/undone/redone change since the last save).</summary>
    public bool IsDirty { get; private set; }

    public bool CanUndo => history.CanUndo;

    public bool CanRedo => history.CanRedo;

    /// <summary>
    /// Paints <paramref name="tileId"/> onto the cell on the given layer. No-ops (returns <c>null</c>)
    /// when the cell already holds that tile — this keeps a click-drag that re-touches the same cell
    /// from stacking redundant history entries. Throws when the layer, tile, or cell is invalid.
    /// </summary>
    public CellChange? PaintCell(int layerIndex, int x, int y, int tileId)
    {
        if (layerIndex < 0 || layerIndex >= Level.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if (!Level.InBounds(x, y))
            return null;
        if (!Level.IsPlaceableTile(tileId))
            throw new ArgumentException($"Tile id {tileId} is not in the level's palette.", nameof(tileId));
        if (Level.GetCell(layerIndex, x, y) == tileId)
            return null;

        var change = history.Execute(new SetCellCommand(layerIndex, x, y, tileId), Level);
        IsDirty = true;
        return change;
    }

    /// <summary>Erases the cell on the given layer (paints the empty marker). No-op when already empty.</summary>
    public CellChange? EraseCell(int layerIndex, int x, int y)
        => PaintCell(layerIndex, x, y, LayerDefinition.EmptyCell);

    /// <summary>Undoes the last edit and returns the cell to refresh, or <c>null</c> when nothing to undo.</summary>
    public CellChange? Undo()
    {
        var change = history.Undo(Level);
        if (change is not null)
            IsDirty = true;
        return change;
    }

    /// <summary>Redoes the last undone edit and returns the cell to refresh, or <c>null</c> when nothing to redo.</summary>
    public CellChange? Redo()
    {
        var change = history.Redo(Level);
        if (change is not null)
            IsDirty = true;
        return change;
    }

    /// <summary>
    /// Serializes the current level to package bytes and marks the session clean. The caller writes the
    /// bytes to the chosen file (file IO stays outside this engine-agnostic core). The dirty flag clears
    /// on the assumption the write succeeds; a failed write should re-mark dirty via <see cref="MarkDirty"/>.
    /// </summary>
    public byte[] Save()
    {
        var bytes = EditableLevelWriter.ToPackageBytes(Level);
        IsDirty = false;
        return bytes;
    }

    /// <summary>Re-marks the session dirty (used if a save write fails after <see cref="Save"/> returned bytes).</summary>
    public void MarkDirty() => IsDirty = true;
}
