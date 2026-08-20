using Uberkarl.Behavior;

namespace Uberkarl.Editor;

/// <summary>Replaces a placed object's own behavior override. Not a cell change — <see cref="Apply"/>/<see cref="Revert"/> return <c>null</c>.</summary>
public sealed class SetObjectBehaviorCommand : IEditCommand
{
    private readonly int index;
    private readonly BehaviorBinding binding;
    private EditableObjectPlacement? previous;

    public SetObjectBehaviorCommand(int index, BehaviorBinding binding)
    {
        this.index = index;
        this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
    }

    public CellChange? Apply(EditableLevel level)
    {
        previous = level.Objects[index];
        level.SetObjectBehavior(index, binding);
        return null;
    }

    public CellChange? Revert(EditableLevel level)
    {
        level.ReplaceObjectAt(index, previous!);
        return null;
    }
}
