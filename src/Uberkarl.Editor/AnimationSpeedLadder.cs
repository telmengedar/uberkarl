namespace Uberkarl.Editor;

/// <summary>
/// The preset ladder a tile's animation speed (frames per second) is stepped through in
/// <c>TileSetEditor</c> (DiVoid #7551 Phase 2, design #7580) — mirrors <see cref="ScrollSpeedLadder"/>'s
/// shape exactly (same clamp-at-the-ends / nearest-preset-snap arithmetic) so the two gamepad steppers in
/// the editor read as one learnable pattern.
/// </summary>
public static class AnimationSpeedLadder
{
    /// <summary>The ordered preset values (frames per second), ascending.</summary>
    public static readonly IReadOnlyList<double> Presets = new[] { 1.0, 2.0, 3.0, 5.0, 8.0, 12.0, 16.0, 24.0 };

    /// <summary>
    /// The next preset from <paramref name="current"/> in the direction of <paramref name="direction"/>
    /// (positive steps up, negative steps down), clamped at the ends. When <paramref name="current"/> is
    /// not itself a preset, the step proceeds from the nearest preset (see <see cref="Snap"/>).
    /// </summary>
    public static double Step(double current, int direction)
    {
        var index = NearestIndex(current);
        var next = Math.Clamp(index + Math.Sign(direction), 0, Presets.Count - 1);
        return Presets[next];
    }

    /// <summary>The preset nearest to an arbitrary <paramref name="value"/>, for displaying a loaded non-preset speed.</summary>
    public static double Snap(double value) => Presets[NearestIndex(value)];

    private static int NearestIndex(double value)
    {
        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < Presets.Count; i++)
        {
            var distance = Math.Abs(Presets[i] - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
