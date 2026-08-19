namespace Uberkarl.Editor;

/// <summary>Removes the placed object at a fixed index from the level. Not a cell change — <see cref="Apply"/>/<see cref="Revert"/> return <c>null</c>.</summary>
public sealed class RemoveObjectCommand : IEditCommand
{
    private readonly int index;
    private EditableObjectPlacement? removed;

    public RemoveObjectCommand(int index)
    {
        this.index = index;
    }

    public CellChange? Apply(EditableLevel level)
    {
        removed = level.RemoveObjectAt(index);
        return null;
    }

    public CellChange? Revert(EditableLevel level)
    {
        level.InsertObject(index, removed!);
        return null;
    }
}
