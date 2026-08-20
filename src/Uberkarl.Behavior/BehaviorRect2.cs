namespace Uberkarl.Behavior;

/// <summary>An axis-aligned rectangle (top-left <see cref="X"/>/<see cref="Y"/>, downward-positive <see cref="Y"/>).</summary>
public readonly record struct BehaviorRect2(double X, double Y, double Width, double Height)
{
    /// <summary>Left edge.</summary>
    public double Left => X;

    /// <summary>Right edge.</summary>
    public double Right => X + Width;

    /// <summary>Top edge.</summary>
    public double Top => Y;

    /// <summary>Bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Horizontal center.</summary>
    public double CenterX => X + Width / 2.0;

    /// <summary>Vertical center.</summary>
    public double CenterY => Y + Height / 2.0;
}
