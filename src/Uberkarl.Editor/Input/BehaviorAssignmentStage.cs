namespace Uberkarl.Editor.Input;

/// <summary>The lifecycle stage of a <see cref="BehaviorAssignmentPicker"/>.</summary>
public enum BehaviorAssignmentStage
{
    /// <summary>Choosing one of the applicable predefined behaviors from the list surface.</summary>
    SelectingPredefined,

    /// <summary>Tuning the current parameter of the chosen predefined.</summary>
    EditingParameter,

    /// <summary>Finished — <see cref="BehaviorAssignmentPicker.Result"/> holds the assembled binding.</summary>
    Complete,

    /// <summary>Cancelled — terminal, no binding was produced.</summary>
    Cancelled,
}
