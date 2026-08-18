namespace Uberkarl.Editor;

/// <summary>Appends a placed object to the level. Not a cell change — <see cref="Apply"/>/<see cref="Revert"/> return <c>null</c>.</summary>
public sealed class PlaceObjectCommand : IEditCommand
{
    private readonly EditableObjectPlacement placement;
    private int insertedAt;

    public PlaceObjectCommand(EditableObjectPlacement placement)
    {
        this.placement = placement ?? throw new ArgumentNullException(nameof(placement));
    }

    public CellChange? Apply(EditableLevel level)
    {
        insertedAt = level.Objects.Count;
        level.InsertObject(insertedAt, placement);
        return null;
    }

    public CellChange? Revert(EditableLevel level)
    {
        level.RemoveObjectAt(insertedAt);
        return null;
    }
}
