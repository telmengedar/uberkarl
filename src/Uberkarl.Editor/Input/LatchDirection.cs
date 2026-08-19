namespace Uberkarl.Editor.Input;

/// <summary>Reduces four directional inputs (left/right/up/down) to the negative/positive pair latched-radial stepping consumes.</summary>
public static class LatchDirection
{
    public static (bool Negative, bool Positive) Reduce(bool left, bool right, bool up, bool down)
        => (left || up, right || down);
}
