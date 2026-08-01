namespace Uberkarl.Editor.Input;

/// <summary>
/// Index arithmetic for cycling a discrete selection with the shoulder-button / prev-next actions: the
/// tile palette, the layer list, and any future ordered list navigated by a single pair of actions. It
/// wraps at the ends (past the last item lands on the first) because on a gamepad there is no scrollbar
/// to run off — wrapping keeps every item reachable with repeated presses. Pure and engine-agnostic so
/// the wrap/empty-list edges are unit-tested without the engine.
/// </summary>
public static class CyclicSelection
{
    /// <summary>
    /// The index one step forward from <paramref name="current"/> in a list of <paramref name="count"/>
    /// items, wrapping to 0 after the last. Returns −1 for an empty list (nothing to select).
    /// </summary>
    public static int Next(int current, int count) => Step(current, count, +1);

    /// <summary>
    /// The index one step back from <paramref name="current"/> in a list of <paramref name="count"/>
    /// items, wrapping to the last after the first. Returns −1 for an empty list (nothing to select).
    /// </summary>
    public static int Prev(int current, int count) => Step(current, count, -1);

    private static int Step(int current, int count, int delta)
    {
        if (count <= 0)
            return -1;

        // Normalise a possibly out-of-range or negative current index into the wrapped result.
        var next = (current + delta) % count;
        if (next < 0)
            next += count;
        return next;
    }
}
