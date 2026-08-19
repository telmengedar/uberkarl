namespace Uberkarl.Editor.Input;

/// <summary>
/// A menu as pure data: an ordered set of entries, rendered and resolved by either the radial or the list
/// surface. No Godot types here, so entry ordering and the entry-to-outcome mapping are unit-tested
/// without the engine.
/// </summary>
public sealed class MenuModel
{
    /// <summary>
    /// The fraction of full-aim magnitude below which the aim counts as "neutral centre" (no entry). A
    /// direction is compared against this after being treated as a unit-ish vector, so 0.35 means "aim must
    /// clear roughly a third of the way out before an entry lights up". Meaningful only for the radial surface.
    /// </summary>
    public const double DefaultDeadzone = 0.35;

    private readonly IReadOnlyList<MenuItem> items;

    public MenuModel(string title, IReadOnlyList<MenuItem> items)
    {
        Title = title;
        this.items = items ?? System.Array.Empty<MenuItem>();
    }

    /// <summary>The menu's caption (e.g. "Tiles", "Layers", "Actions").</summary>
    public string Title { get; }

    /// <summary>The entries, in the order a list renders them / a radial lays them out clockwise from the top.</summary>
    public IReadOnlyList<MenuItem> Items => items;

    /// <summary>How many entries the menu has.</summary>
    public int Count => items.Count;

    /// <summary>
    /// The entry index a direction aims at (<c>-1</c> for the neutral centre or an empty menu), using the
    /// shared <see cref="RadialGeometry"/>. The radial surface calls this each frame from the live aim
    /// vector to know what to highlight.
    /// </summary>
    public int IndexAt(double dx, double dy, double deadzone = DefaultDeadzone) =>
        RadialGeometry.IndexAt(dx, dy, items.Count, deadzone);

    /// <summary>
    /// The outcome a direction would commit, or <c>null</c> when the aim is on the neutral centre / the menu
    /// is empty. This is the routing seam: a direction in, an editor intent out, with no engine involved.
    /// </summary>
    public MenuOutcome? Resolve(double dx, double dy, double deadzone = DefaultDeadzone)
    {
        int index = IndexAt(dx, dy, deadzone);
        return index < 0 ? null : items[index].Outcome;
    }

    /// <summary>The outcome of a known entry index, or <c>null</c> if the index is out of range.</summary>
    public MenuOutcome? OutcomeAt(int index) =>
        index < 0 || index >= items.Count ? null : items[index].Outcome;
}
