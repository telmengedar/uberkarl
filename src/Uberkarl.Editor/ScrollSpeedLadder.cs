namespace Uberkarl.Editor;

/// <summary>
/// The preset ladder <c>scrollSpeed</c> is stepped through instead of free numeric entry (gamepad
/// reality — see the deferred Save-As naming). Stepping clamps at the ends (a magnitude ladder reads
/// more predictably clamped than wrapped); an arbitrary loaded value (a hand-authored package) snaps to
/// its nearest preset for display, and stepping from an off-ladder value proceeds from that snapped
/// position. Per Toni's decision on open question #2, an off-ladder value is preserved on save until the
/// author actually steps it — that policy lives in the session, not here; this type only knows the
/// ladder arithmetic.
/// </summary>
public static class ScrollSpeedLadder
{
    /// <summary>The ordered preset values, ascending.</summary>
    public static readonly IReadOnlyList<float> Presets = new[] { 0.25f, 0.5f, 0.75f, 1.0f, 1.5f, 2.0f };

    /// <summary>
    /// The next preset from <paramref name="current"/> in the direction of <paramref name="direction"/>
    /// (positive steps up, negative steps down), clamped at the ends. When <paramref name="current"/> is
    /// not itself a preset, the step proceeds from the nearest preset (see <see cref="Snap"/>).
    /// </summary>
    public static float Step(float current, int direction)
    {
        int index = NearestIndex(current);
        int next = Math.Clamp(index + Math.Sign(direction), 0, Presets.Count - 1);
        return Presets[next];
    }

    /// <summary>The preset nearest to an arbitrary <paramref name="value"/>, for displaying a loaded non-preset speed.</summary>
    public static float Snap(float value) => Presets[NearestIndex(value)];

    private static int NearestIndex(float value)
    {
        int bestIndex = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < Presets.Count; i++)
        {
            float distance = Math.Abs(Presets[i] - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
