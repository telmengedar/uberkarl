namespace Uberkarl.Editor.Input;

/// <summary>The lifecycle stage of a <see cref="BehaviorAssignmentPicker"/>.</summary>
public enum BehaviorAssignmentStage
{
    /// <summary>Browsing <see cref="BehaviorAssignmentPicker.Choices"/>.</summary>
    SelectingPredefined,

    /// <summary>Tuning the current parameter of the chosen predefined.</summary>
    EditingParameter,

    /// <summary>Waiting for the new script's name.</summary>
    NamingNewScript,

    /// <summary>Finished — <see cref="BehaviorAssignmentPicker.Result"/> holds the assembled binding.</summary>
    Complete,

    /// <summary>Cancelled — terminal, no binding was produced.</summary>
    Cancelled,
}
