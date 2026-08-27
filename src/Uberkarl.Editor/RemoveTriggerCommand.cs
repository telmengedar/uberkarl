using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>Removes the trigger at a fixed index from the level. Not a cell change — <see cref="Apply"/>/<see cref="Revert"/> return <c>null</c>.</summary>
public sealed class RemoveTriggerCommand : IEditCommand
{
    private readonly int index;
    private AreaTriggerDefinition? removed;

    public RemoveTriggerCommand(int index)
    {
        this.index = index;
    }

    public CellChange? Apply(EditableLevel level)
    {
        removed = level.RemoveTriggerAt(index);
        return null;
    }

    public CellChange? Revert(EditableLevel level)
    {
        level.InsertTrigger(index, removed!);
        return null;
    }
}
