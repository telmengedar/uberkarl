namespace Uberkarl.Editor;

/// <summary>Pure two-corner rect math for the trigger tool (design #8049 M4b): normalizes either corner order into a positive-extent rect, then clamps it to a level's grid.</summary>
public static class TriggerRectMath
{
    /// <summary>Normalizes the two corners (either may be the "first" or "second" press, in any relative position) into a positive-width/height rect, then clamps it into the <paramref name="levelWidth"/>x<paramref name="levelHeight"/> grid.</summary>
    public static TriggerRect FromCorners(int x0, int y0, int x1, int y1, int levelWidth, int levelHeight)
    {
        int x = Math.Min(x0, x1);
        int y = Math.Min(y0, y1);
        int width = Math.Abs(x1 - x0) + 1;
        int height = Math.Abs(y1 - y0) + 1;
        return Clamp(new TriggerRect(x, y, width, height), levelWidth, levelHeight);
    }

    /// <summary>Clamps <paramref name="rect"/> so its origin and extent both stay inside a <paramref name="levelWidth"/>x<paramref name="levelHeight"/> grid, never shrinking below 1x1.</summary>
    public static TriggerRect Clamp(TriggerRect rect, int levelWidth, int levelHeight)
    {
        int maxX = Math.Max(levelWidth - 1, 0);
        int maxY = Math.Max(levelHeight - 1, 0);
        int x = Math.Clamp(rect.X, 0, maxX);
        int y = Math.Clamp(rect.Y, 0, maxY);
        int width = Math.Clamp(rect.Width, 1, levelWidth - x);
        int height = Math.Clamp(rect.Height, 1, levelHeight - y);
        return new TriggerRect(x, y, width, height);
    }
}
