using Uberkarl.Behavior;
using Uberkarl.Content;

namespace Uberkarl.Editor;

/// <summary>Sets (adding or replacing) a tile instance's behavior override. Not a cell change — <see cref="Apply"/>/<see cref="Revert"/> return <c>null</c>.</summary>
public sealed class SetTileBehaviorOverrideCommand : IEditCommand
{
    private readonly int layerIndex;
    private readonly int x;
    private readonly int y;
    private readonly BehaviorBinding binding;
    private int replacedIndex = -1;
    private TileBehaviorOverride? previousEntry;

    public SetTileBehaviorOverrideCommand(int layerIndex, int x, int y, BehaviorBinding binding)
    {
        this.layerIndex = layerIndex;
        this.x = x;
        this.y = y;
        this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
    }

    public CellChange? Apply(EditableLevel level)
    {
        replacedIndex = level.FindTileBehaviorOverrideIndex(layerIndex, x, y);
        previousEntry = replacedIndex >= 0 ? level.TileBehaviorOverrides[replacedIndex] : null;
        level.SetTileBehaviorOverride(layerIndex, x, y, binding);
        return null;
    }

    public CellChange? Revert(EditableLevel level)
    {
        if (replacedIndex >= 0)
        {
            level.ReplaceTileBehaviorOverrideAt(replacedIndex, previousEntry!);
        }
        else
        {
            var appendedIndex = level.FindTileBehaviorOverrideIndex(layerIndex, x, y);
            if (appendedIndex >= 0)
                level.RemoveTileBehaviorOverrideAt(appendedIndex);
        }
        return null;
    }
}
