namespace Uberkarl.Editor;

/// <summary>One reversible edit to a level, applied/reverted on the undo/redo stack.</summary>
public interface IEditCommand
{
    /// <summary>Applies the edit to the level and returns the cell that changed, or <c>null</c> for a non-cell edit.</summary>
    CellChange? Apply(EditableLevel level);

    /// <summary>Reverses the edit, restoring the prior value, and returns the cell that changed back, or <c>null</c> for a non-cell edit.</summary>
    CellChange? Revert(EditableLevel level);
}
