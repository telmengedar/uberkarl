namespace Uberkarl.Editor.Input;

/// <summary>One selectable wedge of a radial menu: a human label and the device-neutral outcome it commits.</summary>
public readonly struct RadialMenuItem
{
    public RadialMenuItem(string label, MenuOutcome outcome)
    {
        Label = label;
        Outcome = outcome;
    }

    /// <summary>The wedge's caption (a tool/file name, a layer name, or a tile tag). May be empty for an icon-only tile wedge.</summary>
    public string Label { get; }

    /// <summary>What committing this wedge does, dispatched by the controller onto existing editor operations.</summary>
    public MenuOutcome Outcome { get; }
}

/// <summary>
/// A radial menu as pure data: an ordered set of wedges plus the geometry that turns a pointing direction
/// into the aimed wedge. It is the engine-agnostic model the pop-in surface renders and resolves — the
/// controller builds one from current editor state (the palette, the layer list, the file/action set), the
/// overlay draws it and feeds it the live aim direction, and committing yields the chosen wedge's
/// <see cref="MenuOutcome"/>. No Godot types here, so wedge bucketing, the centre dead-zone, and the
/// wedge-to-outcome mapping ("menu → action routing") are unit-tested without the engine.
/// </summary>
public sealed class RadialMenuModel
{
    /// <summary>
    /// The fraction of full-aim magnitude below which the aim counts as "neutral centre" (no wedge). A
    /// direction is compared against this after being treated as a unit-ish vector, so 0.35 means "aim must
    /// clear roughly a third of the way out before a wedge lights up".
    /// </summary>
    public const double DefaultDeadzone = 0.35;

    private readonly IReadOnlyList<RadialMenuItem> items;

    public RadialMenuModel(string title, IReadOnlyList<RadialMenuItem> items)
    {
        Title = title;
        this.items = items ?? System.Array.Empty<RadialMenuItem>();
    }

    /// <summary>The menu's caption, shown at the neutral centre (e.g. "Tiles", "Layers", "Actions").</summary>
    public string Title { get; }

    /// <summary>The wedges, in clockwise order from the top.</summary>
    public IReadOnlyList<RadialMenuItem> Items => items;

    /// <summary>How many wedges the menu has.</summary>
    public int Count => items.Count;

    /// <summary>
    /// The wedge index a direction aims at (<c>-1</c> for the neutral centre or an empty menu), using the
    /// shared <see cref="RadialGeometry"/>. The overlay calls this each frame from the live aim vector to
    /// know what to highlight.
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

    /// <summary>The outcome of a known wedge index, or <c>null</c> if the index is out of range.</summary>
    public MenuOutcome? OutcomeAt(int index) =>
        index < 0 || index >= items.Count ? null : items[index].Outcome;
}
