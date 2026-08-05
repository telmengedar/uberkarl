namespace Uberkarl.Behavior;

/// <summary>
/// A continuous world/pixel position or velocity (design #7704 §9.4 — placed objects are "grid-placed at
/// author time, free-moving live bodies at runtime", so runtime position cannot be expressed as a
/// <see cref="GridCell"/> alone).
/// </summary>
public readonly record struct BehaviorVector2(double X, double Y)
{
    public static readonly BehaviorVector2 Zero = new(0, 0);
}
