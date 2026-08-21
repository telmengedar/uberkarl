namespace Uberkarl.Editor.Input;

/// <summary>Where the cursor's subject label draws on the canvas, clamped to stay fully inside a given rect.</summary>
public static class CursorLabelAnchor
{
    /// <summary>The gap, in pixels, kept between the cursor's cell and the label.</summary>
    public const double CellGap = 6.0;

    /// <summary>The minimum distance, in pixels, the label is kept from a viewport edge.</summary>
    public const double EdgeMargin = 4.0;

    /// <summary>The label's top-left draw position.</summary>
    /// <param name="cellX">the cursor cell's left edge</param>
    /// <param name="cellY">the cursor cell's top edge</param>
    /// <param name="cellWidth">the cursor cell's width</param>
    /// <param name="cellHeight">the cursor cell's height</param>
    /// <param name="labelWidth">the label's measured width</param>
    /// <param name="labelHeight">the label's measured height</param>
    /// <param name="viewportX">the clamping rect's left edge</param>
    /// <param name="viewportY">the clamping rect's top edge</param>
    /// <param name="viewportWidth">the clamping rect's width</param>
    /// <param name="viewportHeight">the clamping rect's height</param>
    /// <returns>the label's top-left corner, always fully inside the viewport rect</returns>
    public static (double X, double Y) Resolve(
        double cellX, double cellY, double cellWidth, double cellHeight,
        double labelWidth, double labelHeight,
        double viewportX, double viewportY, double viewportWidth, double viewportHeight)
    {
        double above = cellY - CellGap - labelHeight;
        double y = above >= viewportY + EdgeMargin ? above : cellY + cellHeight + CellGap;
        double x = cellX + cellWidth / 2.0 - labelWidth / 2.0;

        return (
            ClampAxis(x, labelWidth, viewportX, viewportWidth),
            ClampAxis(y, labelHeight, viewportY, viewportHeight));
    }

    private static double ClampAxis(double value, double size, double origin, double extent)
    {
        if (size + EdgeMargin * 2 >= extent)
            return origin + (extent - size) / 2.0;
        return System.Math.Clamp(value, origin + EdgeMargin, origin + extent - size - EdgeMargin);
    }
}
