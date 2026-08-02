namespace Uberkarl.Editor.Input;

/// <summary>
/// The pure geometry of a radial (pie) menu: given a pointing direction and a wedge count, which wedge is
/// being aimed at. This is the device-neutral heart of the pop-in menu — a gamepad stick, a keyboard
/// arrow vector, and a mouse offset from the menu centre all reduce to a single <c>(dx, dy)</c> direction,
/// and this decides the highlighted wedge from it. Kept engine-agnostic and pure so the angle bucketing
/// and the centre dead-zone (aim too close to the centre = no selection) are unit-tested without Godot.
///
/// Convention: wedge 0 is centred at the top (screen "up", −Y) and wedges advance clockwise. This matches
/// how the Godot overlay lays the wedges out, so an index here maps straight to a drawn wedge.
/// </summary>
public static class RadialGeometry
{
    /// <summary>A full turn in radians.</summary>
    public const double Tau = System.Math.PI * 2.0;

    /// <summary>
    /// The wedge index a direction points at, for a menu of <paramref name="count"/> wedges. Returns
    /// <c>-1</c> when there is nothing to aim at (<paramref name="count"/> ≤ 0) or the direction's
    /// magnitude is within <paramref name="deadzone"/> (the neutral centre — no wedge selected). The
    /// direction need not be normalised; only its angle and magnitude matter.
    /// </summary>
    public static int IndexAt(double dx, double dy, int count, double deadzone)
    {
        if (count <= 0)
            return -1;

        double magnitude = System.Math.Sqrt((dx * dx) + (dy * dy));
        if (magnitude <= deadzone)
            return -1;

        // atan2(dx, -dy): 0 at "up" (−Y), increasing clockwise (rightward dx is positive).
        double angle = System.Math.Atan2(dx, -dy);
        if (angle < 0)
            angle += Tau;

        double step = Tau / count;
        int index = (int)System.Math.Round(angle / step) % count;
        return index;
    }

    /// <summary>
    /// The centre angle (radians, clockwise from "up") of wedge <paramref name="index"/> in a menu of
    /// <paramref name="count"/> wedges. The overlay uses this to place each wedge's label/icon; feeding a
    /// direction built from this angle back into <see cref="IndexAt"/> returns the same index.
    /// </summary>
    public static double WedgeCenterAngle(int index, int count)
    {
        if (count <= 0)
            return 0.0;
        double step = Tau / count;
        return index * step;
    }

    /// <summary>
    /// A unit direction vector <c>(dx, dy)</c> pointing at the centre of wedge <paramref name="index"/>,
    /// in the same screen convention (up = −Y, clockwise). Convenience for placing wedge content and for
    /// tests that need a direction for a known wedge.
    /// </summary>
    public static (double Dx, double Dy) WedgeDirection(int index, int count)
    {
        double angle = WedgeCenterAngle(index, count);
        return (System.Math.Sin(angle), -System.Math.Cos(angle));
    }
}
