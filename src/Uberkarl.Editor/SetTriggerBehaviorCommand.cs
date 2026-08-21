using Uberkarl.Behavior;
using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>Replaces a trigger's binding. Not a cell change — <see cref="Apply"/>/<see cref="Revert"/> return <c>null</c>.</summary>
public sealed class SetTriggerBehaviorCommand : IEditCommand
{
    private readonly int index;
    private readonly BehaviorBinding binding;
    private AreaTriggerDefinition? previous;

    public SetTriggerBehaviorCommand(int index, BehaviorBinding binding)
    {
        this.index = index;
        this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
    }

    public CellChange? Apply(EditableLevel level)
    {
        previous = level.Triggers[index];
        level.SetTriggerBehavior(index, binding);
        return null;
    }

    public CellChange? Revert(EditableLevel level)
    {
        level.ReplaceTriggerAt(index, previous!);
        return null;
    }
}
