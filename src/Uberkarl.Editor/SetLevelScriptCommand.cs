using Uberkarl.Behavior;

namespace Uberkarl.Editor;

/// <summary>Sets the level's global lifecycle/<c>onUpdate</c> script binding. Not a cell change — <see cref="Apply"/>/<see cref="Revert"/> return <c>null</c>.</summary>
public sealed class SetLevelScriptCommand : IEditCommand
{
    private readonly BehaviorBinding? binding;
    private BehaviorBinding? previous;

    public SetLevelScriptCommand(BehaviorBinding? binding)
    {
        this.binding = binding;
    }

    public CellChange? Apply(EditableLevel level)
    {
        previous = level.LevelScript;
        level.SetLevelScript(binding);
        return null;
    }

    public CellChange? Revert(EditableLevel level)
    {
        level.SetLevelScript(previous);
        return null;
    }
}
