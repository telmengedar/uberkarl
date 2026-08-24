namespace Uberkarl.Editor;

/// <summary>The two-corner trigger placement mode's pure state machine (design #8049 M4b).</summary>
public sealed class TriggerRectTool
{
    private (int X, int Y)? firstCorner;

    /// <summary>True while a first corner is recorded and a second press would complete the rect.</summary>
    public bool HasPendingCorner => firstCorner is not null;

    /// <summary>Presses cell (<paramref name="x"/>,<paramref name="y"/>) — the first press records it, the second completes the rect against <paramref name="levelWidth"/>x<paramref name="levelHeight"/> and clears the pending corner.</summary>
    public TriggerRectResult Press(int x, int y, int levelWidth, int levelHeight)
    {
        if (firstCorner is not { } corner)
        {
            firstCorner = (x, y);
            return TriggerRectResult.CornerStarted();
        }

        firstCorner = null;
        return TriggerRectResult.ForRect(TriggerRectMath.FromCorners(corner.X, corner.Y, x, y, levelWidth, levelHeight));
    }

    /// <summary>Cancels a pending first corner. Returns <c>false</c> (no-op) when none is pending, so a caller can skip redrawing a preview that was never shown.</summary>
    public bool CancelPending()
    {
        if (firstCorner is null)
            return false;

        firstCorner = null;
        return true;
    }

    /// <summary>The live preview rect from the pending first corner to (<paramref name="x"/>,<paramref name="y"/>), or <c>null</c> when no corner is pending. Pure query — never mutates the tool's state.</summary>
    public TriggerRect? PreviewRect(int x, int y, int levelWidth, int levelHeight) =>
        firstCorner is { } corner ? TriggerRectMath.FromCorners(corner.X, corner.Y, x, y, levelWidth, levelHeight) : null;
}
