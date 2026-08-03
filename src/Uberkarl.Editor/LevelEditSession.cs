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

    // ----- layer management intents -----
    //
    // History policy (the layer-index aliasing hazard, design §9.3): recorded SetCellCommands store an
    // absolute layer index. AddLayer appends at the end and SetCollision/StepScrollSpeed/SetRepeat replace
    // a layer in place — both are index-stable, so cell-edit history is preserved. DeleteLayer and
    // MoveLayer shift indices, so a successful one clears cell-edit history (the same Clear() used on
    // load/save-as) rather than let recorded undo alias onto the wrong layer. Layer operations themselves
    // are not on the undo stack this increment.

    /// <summary>
    /// Appends a new auto-named ("Layer N") display layer (<c>collision:false, scrollSpeed:1.0, repeat:false</c>)
    /// at the front-of-array end. Index-stable, so cell-edit history is preserved.
    /// </summary>
    public LayerEditResult AddLayer()
    {
        var name = LayerNaming.NextName(Level.Layers.Select(layer => layer.Name));
        var index = Level.AppendLayer(name, collision: false, scrollSpeed: 1.0f, repeat: false);
        IsDirty = true;
        return new LayerEditResult(true, index);
    }

    /// <summary>
    /// Deletes the layer at <paramref name="index"/>. No-op (returns <c>Happened:false</c>) when
    /// <paramref name="index"/> is out of range or it is the level's last layer. A real delete clears
    /// cell-edit history and reconciles the returned index into the remaining layer range (clamped).
    /// </summary>
    public LayerEditResult DeleteLayer(int index)
    {
        if (!Level.RemoveLayerAt(index))
            return new LayerEditResult(false, index);

        history.Clear();
        IsDirty = true;
        var reconciled = Math.Clamp(index, 0, Level.Layers.Count - 1);
        return new LayerEditResult(true, reconciled);
    }

    /// <summary>
    /// Moves the layer at <paramref name="index"/> one step in <paramref name="direction"/> (positive =
    /// toward the front, negative = toward the back) — an adjacent swap that changes draw order. No-op at
    /// either end. A real move clears cell-edit history; the returned index is the moved layer's new
    /// position, so the controller can keep it the active layer.
    /// </summary>
    public LayerEditResult MoveLayer(int index, int direction)
    {
        if (index < 0 || index >= Level.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var newIndex = Level.MoveLayer(index, direction);
        if (newIndex == index)
            return new LayerEditResult(false, index);

        history.Clear();
        IsDirty = true;
        return new LayerEditResult(true, newIndex);
    }

    /// <summary>
    /// Sets the collision flag on the layer at <paramref name="index"/>. Collision is dominant: turning
    /// it on coerces scroll speed to 1.0 and repeat to off (<see cref="LayerPropertyRules"/>). Always
    /// reachable (this is the control that governs whether scroll/repeat are editable at all).
    /// Index-stable, so cell-edit history is preserved.
    /// </summary>
    public LayerEditResult SetCollision(int index, bool collision)
    {
        var layer = LayerAt(index);
        var happened = Level.SetLayerProperties(index, collision, layer.ScrollSpeed, layer.Repeat);
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    /// <summary>
    /// Steps the layer at <paramref name="index"/>'s scroll speed through the preset ladder
    /// (<see cref="ScrollSpeedLadder"/>). No-op while the layer's collision flag is on (scroll is locked
    /// to 1.0 then — <see cref="LayerPropertyRules.Editable"/>). Index-stable, so cell-edit history is
    /// preserved.
    /// </summary>
    public LayerEditResult StepScrollSpeed(int index, int direction)
    {
        var layer = LayerAt(index);
        if (!LayerPropertyRules.Editable(layer.Collision))
            return new LayerEditResult(false, index);

        var stepped = ScrollSpeedLadder.Step(layer.ScrollSpeed, direction);
        var happened = Level.SetLayerProperties(index, layer.Collision, stepped, layer.Repeat);
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    /// <summary>
    /// Sets the layer at <paramref name="index"/>'s scroll speed to an absolute <paramref name="scrollSpeed"/>
    /// (as opposed to <see cref="StepScrollSpeed"/>'s relative ladder step). The panel's edit-mode stepper
    /// (DiVoid #7512) uses this: it steps a local pending value through <see cref="ScrollSpeedLadder"/>
    /// without touching the model, then applies the final value here on commit only — so a cancelled edit
    /// never reaches the model and needs no revert. No-op while the layer's collision flag is on (scroll is
    /// locked to 1.0 then — <see cref="LayerPropertyRules.Editable"/>). Index-stable, so cell-edit history
    /// is preserved.
    /// </summary>
    public LayerEditResult SetScrollSpeed(int index, float scrollSpeed)
    {
        var layer = LayerAt(index);
        if (!LayerPropertyRules.Editable(layer.Collision))
            return new LayerEditResult(false, index);

        var happened = Level.SetLayerProperties(index, layer.Collision, scrollSpeed, layer.Repeat);
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    /// <summary>
    /// Sets the repeat flag on the layer at <paramref name="index"/>. No-op while the layer's collision
    /// flag is on (repeat is locked off then). Index-stable, so cell-edit history is preserved.
    /// </summary>
    public LayerEditResult SetRepeat(int index, bool repeat)
    {
        var layer = LayerAt(index);
        if (!LayerPropertyRules.Editable(layer.Collision))
            return new LayerEditResult(false, index);

        var happened = Level.SetLayerProperties(index, layer.Collision, layer.ScrollSpeed, repeat);
        if (happened)
            IsDirty = true;
        return new LayerEditResult(happened, index);
    }

    private EditableLayer LayerAt(int index)
    {
        if (index < 0 || index >= Level.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Level.Layers[index];
    }
}
