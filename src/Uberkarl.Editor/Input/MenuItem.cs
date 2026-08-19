namespace Uberkarl.Editor.Input;

/// <summary>One selectable entry of a menu: a human label and the device-neutral outcome it commits.</summary>
public readonly struct MenuItem
{
    public MenuItem(string label, MenuOutcome outcome)
    {
        Label = label;
        Outcome = outcome;
    }

    /// <summary>The entry's caption (a tool/file name, a layer name, or a tile tag).</summary>
    public string Label { get; }

    /// <summary>What committing this entry does, dispatched by the controller onto existing editor operations.</summary>
    public MenuOutcome Outcome { get; }
}
