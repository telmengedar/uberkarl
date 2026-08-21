namespace Uberkarl.Editor.Input;

/// <summary>Which segment of <see cref="BehaviorAssignmentPicker.Choices"/> a row belongs to.</summary>
public enum BehaviorAssignmentChoiceKind
{
    /// <summary>An applicable predefined behavior.</summary>
    Predefined,

    /// <summary>An already-authored script.</summary>
    ExistingScript,

    /// <summary>The trailing "create a new script" row.</summary>
    NewScript,
}

/// <summary>One row of <see cref="BehaviorAssignmentPicker.Choices"/>.</summary>
public readonly struct BehaviorAssignmentChoice
{
    private BehaviorAssignmentChoice(BehaviorAssignmentChoiceKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    /// <summary>Which segment this choice belongs to.</summary>
    public BehaviorAssignmentChoiceKind Kind { get; }

    /// <summary>The display text for this row.</summary>
    public string Label { get; }

    /// <summary>A predefined row.</summary>
    public static BehaviorAssignmentChoice ForPredefined(string label) => new(BehaviorAssignmentChoiceKind.Predefined, label);

    /// <summary>An existing-script row.</summary>
    public static BehaviorAssignmentChoice ForExistingScript(string label) => new(BehaviorAssignmentChoiceKind.ExistingScript, label);

    /// <summary>The trailing "create a new script" row.</summary>
    public static BehaviorAssignmentChoice ForNewScript(string label) => new(BehaviorAssignmentChoiceKind.NewScript, label);
}
