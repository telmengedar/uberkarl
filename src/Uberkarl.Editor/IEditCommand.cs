namespace Uberkarl.Editor;

/// <summary>
/// One reversible edit to a level. Commands are the undo/redo seam: every mutation the UI performs is
/// expressed as a command so it can be applied, reverted, and (for a redo) re-applied. Each command
/// reports the single cell it affects so the canvas can update in place. Later increments add more
/// command kinds (fill, rectangle, layer/spawn edits) without changing this contract.
/// </summary>
public interface IEditCommand
{
    /// <summary>Applies the edit to the level and returns the cell that changed.</summary>
    CellChange Apply(EditableLevel level);

    /// <summary>Reverses the edit, restoring the prior value, and returns the cell that changed back.</summary>
    CellChange Revert(EditableLevel level);
}
