namespace Uberkarl.Editor.Input;

/// <summary>
/// Where a pop-in menu centres: the pointer's own position while it is driving the grid cursor, or the
/// cursor cell's centre otherwise — then clamped so a disc of the menu's radius stays fully inside the
/// viewport.
/// </summary>
public static class MenuAnchor
{
    /// <summary>The menu centre before clamping.</summary>
    public static (double X, double Y) Resolve(bool pointerDrivesCursor, double pointerX, double pointerY, double cursorCenterX, double cursorCenterY) =>
        pointerDrivesCursor ? (pointerX, pointerY) : (cursorCenterX, cursorCenterY);

    /// <summary>Clamps <paramref name="x"/>/<paramref name="y"/> so a disc of <paramref name="margin"/>
    /// radius around it stays inside the <paramref name="viewportWidth"/>×<paramref name="viewportHeight"/>
    /// rect at (<paramref name="viewportX"/>, <paramref name="viewportY"/>), centring on an axis too small
    /// to hold it.</summary>
    public static (double X, double Y) Clamp(double x, double y, double viewportX, double viewportY, double viewportWidth, double viewportHeight, double margin) =>
        (ClampAxis(x, viewportX, viewportWidth, margin), ClampAxis(y, viewportY, viewportHeight, margin));

    private static double ClampAxis(double value, double origin, double extent, double margin)
    {
        if (margin * 2 >= extent)
            return origin + extent / 2.0;
        return System.Math.Clamp(value, origin + margin, origin + extent - margin);
    }
}
