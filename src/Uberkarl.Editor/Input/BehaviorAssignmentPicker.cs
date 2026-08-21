using Uberkarl.Behavior;

namespace Uberkarl.Editor.Input;

/// <summary>
/// Engine-agnostic sequencing for "assign a predefined behavior and tune its parameters" (design #8049 §7,
/// #8525 §12): picks one of the predefineds applicable to a subject kind from the list surface, then tunes
/// each of its parameters via <see cref="SteppedValueEditor{T}"/>. A predefined with no parameters commits
/// immediately. <see cref="Result"/> is returned to the caller rather than mutating a subject in place.
/// </summary>
public sealed class BehaviorAssignmentPicker
{
    private readonly Dictionary<string, double> values = new();
    private int parameterIndex = -1;
    private SteppedValueEditor<double>? parameterEditor;

    public BehaviorAssignmentPicker(BehaviorSubjectKind subjectKind)
    {
        SubjectKind = subjectKind;
        ApplicablePredefineds = PredefinedBehaviors.ApplicableTo(subjectKind).ToList();
        Stage = BehaviorAssignmentStage.SelectingPredefined;
    }

    /// <summary>The subject kind this picker was constructed for.</summary>
    public BehaviorSubjectKind SubjectKind { get; }

    /// <summary>The predefineds applicable to <see cref="SubjectKind"/>, in the order the list surface renders them.</summary>
    public IReadOnlyList<PredefinedBehaviorDescriptor> ApplicablePredefineds { get; }

    /// <summary>The picker's current lifecycle stage.</summary>
    public BehaviorAssignmentStage Stage { get; private set; }

    /// <summary>The predefined chosen via <see cref="SelectPredefined"/>, or <c>null</c> before a pick is made.</summary>
    public PredefinedBehaviorDescriptor? Selected { get; private set; }

    /// <summary>The parameter currently being tuned, or <c>null</c> outside <see cref="BehaviorAssignmentStage.EditingParameter"/>.</summary>
    public PredefinedParameterDescriptor? CurrentParameter =>
        Stage == BehaviorAssignmentStage.EditingParameter ? Selected!.Parameters[parameterIndex] : null;

    /// <summary>The value the current parameter would commit at right now. Meaningless outside <see cref="BehaviorAssignmentStage.EditingParameter"/>.</summary>
    public double CurrentParameterPendingValue => parameterEditor?.PendingValue ?? 0;

    /// <summary>The finished binding, set once <see cref="Stage"/> reaches <see cref="BehaviorAssignmentStage.Complete"/>.</summary>
    public BehaviorBinding? Result { get; private set; }

    /// <summary>
    /// Picks the predefined at <paramref name="index"/> into <see cref="ApplicablePredefineds"/>, seeds every
    /// parameter at its descriptor default, and advances to the first parameter (or straight to
    /// <see cref="BehaviorAssignmentStage.Complete"/> when the predefined has none). No-op (returns
    /// <c>false</c>) outside <see cref="BehaviorAssignmentStage.SelectingPredefined"/> or for an out-of-range index.
    /// </summary>
    public bool SelectPredefined(int index)
    {
        if (Stage != BehaviorAssignmentStage.SelectingPredefined || index < 0 || index >= ApplicablePredefineds.Count)
            return false;

        Selected = ApplicablePredefineds[index];
        values.Clear();
        foreach (PredefinedParameterDescriptor parameter in Selected.Parameters)
            values[parameter.Name] = parameter.Default;

        parameterIndex = -1;
        AdvanceParameter();
        return true;
    }

    /// <summary>Steps the current parameter's pending value via its descriptor. No-op outside <see cref="BehaviorAssignmentStage.EditingParameter"/>.</summary>
    public bool AdjustCurrentParameter(int direction) =>
        Stage == BehaviorAssignmentStage.EditingParameter && parameterEditor!.Adjust(direction);

    /// <summary>
    /// Commits the current parameter's pending value and advances to the next parameter, or to
    /// <see cref="BehaviorAssignmentStage.Complete"/> when it was the last. No-op outside
    /// <see cref="BehaviorAssignmentStage.EditingParameter"/>.
    /// </summary>
    public bool CommitCurrentParameter()
    {
        if (Stage != BehaviorAssignmentStage.EditingParameter || !parameterEditor!.TryCommit(out double value))
            return false;

        values[Selected!.Parameters[parameterIndex].Name] = value;
        parameterEditor = null;
        AdvanceParameter();
        return true;
    }

    /// <summary>Cancels the pick from any stage. Terminal — a new picker is constructed to try again.</summary>
    public void Cancel() => Stage = BehaviorAssignmentStage.Cancelled;

    private void AdvanceParameter()
    {
        parameterIndex++;
        if (parameterIndex >= Selected!.Parameters.Count)
        {
            Result = BehaviorBinding.FromPredefined(Selected.Id, values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
            Stage = BehaviorAssignmentStage.Complete;
            return;
        }

        PredefinedParameterDescriptor parameter = Selected.Parameters[parameterIndex];
        parameterEditor = new SteppedValueEditor<double>(parameter.Step);
        parameterEditor.Enter(values[parameter.Name]);
        Stage = BehaviorAssignmentStage.EditingParameter;
    }
}
