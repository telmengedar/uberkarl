namespace Uberkarl.Editor;

/// <summary>
/// Clamped step arithmetic for a level's width/height in the resize panel's gamepad-friendly steppers
/// (DiVoid #7550) — the same shape as <see cref="ScrollSpeedLadder"/> (an engine-agnostic rule the
/// Godot-side stepper Control drives), but stepping by a fixed increment of 1 across a plain clamped
/// integer range rather than a short curated preset list, since a grid dimension is a continuous count of
/// cells rather than a handful of meaningful speeds. <see cref="MaxDimension"/> is a UI-side soft cap only
/// — not a model invariant (<see cref="EditableLevel.Resize"/> itself only rejects non-positive sizes) —
/// so a hand-authored or previously-saved level larger than the cap still loads and displays fine; the cap
/// only stops a held gamepad stepper from growing the grid without bound.
/// </summary>
public static class GridDimensionRules
{
    /// <summary>The smallest a grid dimension may be stepped down to — a level must always have at least one cell.</summary>
    public const int MinDimension = 1;

    /// <summary>The largest a grid dimension may be stepped up to via the panel's stepper.</summary>
    public const int MaxDimension = 500;

    /// <summary>
    /// Steps <paramref name="current"/> by one cell in the direction of <paramref name="direction"/>
    /// (positive grows, negative shrinks), clamped to <see cref="MinDimension"/>/<see cref="MaxDimension"/>.
    /// </summary>
    public static int Step(int current, int direction) =>
        Math.Clamp(current + Math.Sign(direction), MinDimension, MaxDimension);
}
