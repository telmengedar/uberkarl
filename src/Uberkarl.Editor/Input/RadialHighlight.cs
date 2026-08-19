namespace Uberkarl.Editor.Input;

/// <summary>Discrete highlight stepping for a latched radial menu, defined from the -1 "nothing highlighted" start.</summary>
public static class RadialHighlight
{
    public static int Step(int highlighted, int count, int direction)
    {
        if (count <= 0)
            return -1;
        if (highlighted < 0)
            return direction > 0 ? 0 : count - 1;
        return direction > 0 ? CyclicSelection.Next(highlighted, count) : CyclicSelection.Prev(highlighted, count);
    }
}
