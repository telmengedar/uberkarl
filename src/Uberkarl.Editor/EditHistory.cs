namespace Uberkarl.Editor;

/// <summary>
/// The undo/redo stack. Executing a command applies it and pushes it onto the undo stack, clearing the
/// redo stack (the standard linear-history model). Undo reverts the top command and moves it to redo;
/// redo re-applies it. Depth is bounded so a long painting session cannot grow memory without limit —
/// once the cap is reached the oldest command is dropped (it simply stops being undoable). Deeper or
/// branching history is a later concern; this seam does not change when that lands.
/// </summary>
public sealed class EditHistory
{
    /// <summary>Maximum number of undoable commands retained. Oldest are dropped past this depth.</summary>
    public const int MaxDepth = 500;

    private readonly LinkedList<IEditCommand> undo = new();
    private readonly Stack<IEditCommand> redo = new();

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    /// <summary>Applies a command, records it for undo, and clears the redo stack.</summary>
    public CellChange Execute(IEditCommand command, EditableLevel level)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        var change = command.Apply(level);
        undo.AddLast(command);
        if (undo.Count > MaxDepth)
            undo.RemoveFirst();
        redo.Clear();
        return change;
    }

    /// <summary>Reverts the most recent command, or returns <c>null</c> when there is nothing to undo.</summary>
    public CellChange? Undo(EditableLevel level)
    {
        if (undo.Count == 0)
            return null;

        var command = undo.Last!.Value;
        undo.RemoveLast();
        var change = command.Revert(level);
        redo.Push(command);
        return change;
    }

    /// <summary>Re-applies the most recently undone command, or returns <c>null</c> when there is nothing to redo.</summary>
    public CellChange? Redo(EditableLevel level)
    {
        if (redo.Count == 0)
            return null;

        var command = redo.Pop();
        var change = command.Apply(level);
        undo.AddLast(command);
        return change;
    }

    /// <summary>Discards all history. Called after a save-as/load that replaces the level being edited.</summary>
    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
