using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>Appends a trigger to the level. Not a cell change — <see cref="Apply"/>/<see cref="Revert"/> return <c>null</c>.</summary>
public sealed class PlaceTriggerCommand : IEditCommand
{
    private readonly AreaTriggerDefinition trigger;
    private int insertedAt;

    public PlaceTriggerCommand(AreaTriggerDefinition trigger)
    {
        this.trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
    }

    public CellChange? Apply(EditableLevel level)
    {
        insertedAt = level.Triggers.Count;
        level.InsertTrigger(insertedAt, trigger);
        return null;
    }

    public CellChange? Revert(EditableLevel level)
    {
        level.RemoveTriggerAt(insertedAt);
        return null;
    }
}
