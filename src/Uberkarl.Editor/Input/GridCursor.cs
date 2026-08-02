namespace Uberkarl.Editor.Input;

/// <summary>
/// A highlighted grid cell the user moves around and acts on — the device-neutral equivalent of a mouse
/// pointer for a gamepad or keyboard, which have no pointer. It holds only a cell position and the grid
/// bounds it is confined to; movement is always clamped inside the grid. Engine-agnostic and pure so the
/// clamping/edge behaviour is unit-tested without the engine. The canvas owns one of these and drives it
/// from the cursor-move actions; the mouse keeps it coherent by snapping it to the clicked/hovered cell.
/// </summary>
public sealed class GridCursor
{
    public GridCursor(int width, int height)
    {
        Resize(width, height);
    }

    /// <summary>Current cursor cell X (column), always within <c>[0, Width)</c>.</summary>
    public int X { get; private set; }

    /// <summary>Current cursor cell Y (row), always within <c>[0, Height)</c>.</summary>
    public int Y { get; private set; }

    /// <summary>Grid width in cells.</summary>
    public int Width { get; private set; }

    /// <summary>Grid height in cells.</summary>
    public int Height { get; private set; }

    /// <summary>
    /// Moves the cursor by a cell delta, clamped to the grid. Returns <c>true</c> only when the cursor
    /// actually changed cell — so a move that is already against an edge is a no-op the caller can ignore
    /// (no needless redraw, no edge "buzz").
    /// </summary>
    public bool TryMove(int dx, int dy) => MoveTo(X + dx, Y + dy);

    /// <summary>
    /// Places the cursor at a cell, clamped to the grid. Returns <c>true</c> when the resulting cell
    /// differs from the current one. Used by the mouse to keep the shared cursor on the clicked cell.
    /// </summary>
    public bool MoveTo(int x, int y)
    {
        var clampedX = Clamp(x, Width);
        var clampedY = Clamp(y, Height);
        if (clampedX == X && clampedY == Y)
            return false;

        X = clampedX;
        Y = clampedY;
        return true;
    }

    /// <summary>
    /// Rebinds the grid bounds (a new level was loaded / resized) and re-clamps the cursor so it can
    /// never point outside the new grid. Keeps the cursor's relative position where possible.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive.");

        Width = width;
        Height = height;
        X = Clamp(X, width);
        Y = Clamp(Y, height);
    }

    private static int Clamp(int value, int size)
    {
        if (value < 0)
            return 0;
        if (value >= size)
            return size - 1;
        return value;
    }
}
