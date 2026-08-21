using Uberkarl.Behavior;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Input;

/// <summary>Engine-agnostic sequencing for picking and, where applicable, tuning or naming a behavior assignment.</summary>
public sealed class BehaviorAssignmentPicker
{
    private readonly Dictionary<string, double> values = new();
    private int parameterIndex = -1;
    private SteppedValueEditor<double>? parameterEditor;

    public BehaviorAssignmentPicker(BehaviorSubjectKind subjectKind, IReadOnlyList<ResourcePath>? existingScripts = null)
    {
        SubjectKind = subjectKind;
        ApplicablePredefineds = PredefinedBehaviors.ApplicableTo(subjectKind).ToList();
        ExistingScripts = existingScripts ?? Array.Empty<ResourcePath>();
        Choices = BuildChoices(ApplicablePredefineds, ExistingScripts);
        Stage = BehaviorAssignmentStage.SelectingPredefined;
    }

    /// <summary>The subject kind this picker was constructed for.</summary>
    public BehaviorSubjectKind SubjectKind { get; }

    /// <summary>The predefineds applicable to <see cref="SubjectKind"/>.</summary>
    public IReadOnlyList<PredefinedBehaviorDescriptor> ApplicablePredefineds { get; }

    /// <summary>The level's already-authored script paths this picker was constructed with.</summary>
    public IReadOnlyList<ResourcePath> ExistingScripts { get; }

    /// <summary>The ordered choice list the list surface renders: every predefined, then every existing script, then one "new script" row.</summary>
    public IReadOnlyList<BehaviorAssignmentChoice> Choices { get; }

    /// <summary>The picker's current lifecycle stage.</summary>
    public BehaviorAssignmentStage Stage { get; private set; }

    /// <summary>The predefined chosen via <see cref="SelectPredefined"/>/<see cref="SelectChoice"/>, or <c>null</c> before a pick is made.</summary>
    public PredefinedBehaviorDescriptor? Selected { get; private set; }

    /// <summary>The parameter currently being tuned, or <c>null</c> outside <see cref="BehaviorAssignmentStage.EditingParameter"/>.</summary>
    public PredefinedParameterDescriptor? CurrentParameter =>
        Stage == BehaviorAssignmentStage.EditingParameter ? Selected!.Parameters[parameterIndex] : null;

    /// <summary>The value the current parameter would commit at right now.</summary>
    public double CurrentParameterPendingValue => parameterEditor?.PendingValue ?? 0;

    /// <summary>The finished binding, set once <see cref="Stage"/> reaches <see cref="BehaviorAssignmentStage.Complete"/>.</summary>
    public BehaviorBinding? Result { get; private set; }

    /// <summary>The freshly-minted script path, set only by a completed <see cref="CreateNewScript"/> call; <c>null</c> otherwise.</summary>
    public ResourcePath? MintedScriptPath { get; private set; }

    /// <summary>The starter template text seeded alongside <see cref="MintedScriptPath"/>; <c>null</c> otherwise.</summary>
    public string? MintedScriptSource { get; private set; }

    /// <summary>Picks the predefined at <paramref name="index"/> into <see cref="ApplicablePredefineds"/>. No-op outside <see cref="BehaviorAssignmentStage.SelectingPredefined"/> or for an out-of-range index.</summary>
    public bool SelectPredefined(int index)
    {
        if (Stage != BehaviorAssignmentStage.SelectingPredefined || index < 0 || index >= ApplicablePredefineds.Count)
            return false;

        return CommitPredefined(index);
    }

    /// <summary>Picks the row at <paramref name="index"/> into <see cref="Choices"/>. No-op outside <see cref="BehaviorAssignmentStage.SelectingPredefined"/> or for an out-of-range index.</summary>
    public bool SelectChoice(int index)
    {
        if (Stage != BehaviorAssignmentStage.SelectingPredefined || index < 0 || index >= Choices.Count)
            return false;

        var choice = Choices[index];
        return choice.Kind switch
        {
            BehaviorAssignmentChoiceKind.Predefined => CommitPredefined(index),
            BehaviorAssignmentChoiceKind.ExistingScript => CommitExistingScript(index - ApplicablePredefineds.Count),
            BehaviorAssignmentChoiceKind.NewScript => CommitNewScriptChoice(),
            _ => false,
        };
    }

    /// <summary>Completes the new-script branch: allocates a unique slug path from <paramref name="name"/>, seeds the starter template, and produces the binding. No-op outside <see cref="BehaviorAssignmentStage.NamingNewScript"/> or for a blank <paramref name="name"/>.</summary>
    public bool CreateNewScript(string name, Func<string, bool> isSlugTaken)
    {
        if (isSlugTaken is null)
            throw new ArgumentNullException(nameof(isSlugTaken));
        if (Stage != BehaviorAssignmentStage.NamingNewScript)
            return false;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var baseSlug = ScriptResourcePaths.Slugify(name);
        var slug = ScriptResourcePaths.UniqueSlug(baseSlug, isSlugTaken);
        var path = ScriptResourcePaths.ScriptPath(slug);

        MintedScriptPath = path;
        MintedScriptSource = BehaviorScriptTemplates.For(SubjectKind);
        Result = BehaviorBinding.FromScript(ResourceReference.ToSelf(path));
        Stage = BehaviorAssignmentStage.Complete;
        return true;
    }

    /// <summary>Steps the current parameter's pending value via its descriptor. No-op outside <see cref="BehaviorAssignmentStage.EditingParameter"/>.</summary>
    public bool AdjustCurrentParameter(int direction) =>
        Stage == BehaviorAssignmentStage.EditingParameter && parameterEditor!.Adjust(direction);

    /// <summary>Commits the current parameter's pending value and advances to the next parameter, or to <see cref="BehaviorAssignmentStage.Complete"/> when it was the last.</summary>
    public bool CommitCurrentParameter()
    {
        if (Stage != BehaviorAssignmentStage.EditingParameter || !parameterEditor!.TryCommit(out double value))
            return false;

        values[Selected!.Parameters[parameterIndex].Name] = value;
        parameterEditor = null;
        AdvanceParameter();
        return true;
    }

    /// <summary>Cancels the pick from any stage. Terminal.</summary>
    public void Cancel() => Stage = BehaviorAssignmentStage.Cancelled;

    /// <summary>Returns to <see cref="BehaviorAssignmentStage.SelectingPredefined"/> from <see cref="BehaviorAssignmentStage.NamingNewScript"/>, so a cancelled naming step can retry from the still-open list rather than aborting the whole pick. No-op outside <see cref="BehaviorAssignmentStage.NamingNewScript"/>.</summary>
    public bool CancelNewScriptNaming()
    {
        if (Stage != BehaviorAssignmentStage.NamingNewScript)
            return false;

        Stage = BehaviorAssignmentStage.SelectingPredefined;
        return true;
    }

    private bool CommitPredefined(int index)
    {
        Selected = ApplicablePredefineds[index];
        values.Clear();
        foreach (PredefinedParameterDescriptor parameter in Selected.Parameters)
            values[parameter.Name] = parameter.Default;

        parameterIndex = -1;
        AdvanceParameter();
        return true;
    }

    private bool CommitExistingScript(int scriptIndex)
    {
        Result = BehaviorBinding.FromScript(ResourceReference.ToSelf(ExistingScripts[scriptIndex]));
        Stage = BehaviorAssignmentStage.Complete;
        return true;
    }

    private bool CommitNewScriptChoice()
    {
        Stage = BehaviorAssignmentStage.NamingNewScript;
        return true;
    }

    private static IReadOnlyList<BehaviorAssignmentChoice> BuildChoices(
        IReadOnlyList<PredefinedBehaviorDescriptor> predefineds, IReadOnlyList<ResourcePath> existingScripts)
    {
        var choices = new List<BehaviorAssignmentChoice>(predefineds.Count + existingScripts.Count + 1);
        foreach (var predefined in predefineds)
            choices.Add(BehaviorAssignmentChoice.ForPredefined(predefined.Label));
        foreach (var path in existingScripts)
            choices.Add(BehaviorAssignmentChoice.ForExistingScript(ScriptResourcePaths.SlugFromScriptPath(path) ?? path.Value));
        choices.Add(BehaviorAssignmentChoice.ForNewScript("＋ New script…"));
        return choices;
    }

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
