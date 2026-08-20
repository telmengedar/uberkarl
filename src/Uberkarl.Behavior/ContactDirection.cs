namespace Uberkarl.Behavior;

/// <summary>Classifies which side of one rect another rect touched.</summary>
public static class ContactDirection
{
    /// <summary>The left side was touched.</summary>
    public const string Left = "left";

    /// <summary>The right side was touched.</summary>
    public const string Right = "right";

    /// <summary>The top side was touched.</summary>
    public const string Above = "above";

    /// <summary>The bottom side was touched.</summary>
    public const string Below = "below";

    /// <summary>The side of <paramref name="self"/> that <paramref name="other"/> touched; an exact tie resolves to the horizontal axis.</summary>
    /// <param name="self">The subject's rect. Must overlap <paramref name="other"/> on both axes.</param>
    /// <param name="other">The contacting party's rect. Must overlap <paramref name="self"/> on both axes.</param>
    /// <returns>One of <see cref="Left"/>, <see cref="Right"/>, <see cref="Above"/>, <see cref="Below"/>.</returns>
    public static string Classify(BehaviorRect2 self, BehaviorRect2 other)
    {
        double penetrationX = Math.Min(self.Right, other.Right) - Math.Max(self.Left, other.Left);
        double penetrationY = Math.Min(self.Bottom, other.Bottom) - Math.Max(self.Top, other.Top);

        if (penetrationX <= penetrationY)
            return other.CenterX < self.CenterX ? Left : Right;

        return other.CenterY < self.CenterY ? Above : Below;
    }
}
