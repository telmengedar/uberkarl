using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Covers <see cref="BehaviorAssignmentPicker"/>'s pure sequencing (design #8049 §7/M4-addendum — the
/// picker returns a <see cref="BehaviorBinding"/> to its caller rather than mutating a subject in place):
/// applicability filtering per subject kind, single- and multi-parameter walks, the zero-parameter and
/// zero-applicable-predefined edge cases, and out-of-stage no-ops.
/// </summary>
[TestFixture]
public sealed class BehaviorAssignmentPickerTests
{
    [Test]
    public void Constructed_ForObject_ListsExactlyTheApplicablePredefineds()
    {
        BehaviorAssignmentPicker picker = new(BehaviorSubjectKind.Object);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.SelectingPredefined));
            Assert.That(picker.ApplicablePredefineds.Select(d => d.Id),
                Is.EqualTo(new[] { "hurtOnContact", "patrol", "bumpOnHitFromBelow" }));
        });
    }

    [Test]
    [Description("No predefined in today's library dispatches to the level script, so the picker must show an empty list rather than throw.")]
    public void Constructed_ForLevelScript_ListsNoPredefineds()
    {
        BehaviorAssignmentPicker picker = new(BehaviorSubjectKind.LevelScript);

        Assert.That(picker.ApplicablePredefineds, Is.Empty);
        Assert.That(picker.SelectPredefined(0), Is.False, "picking out of an empty list must no-op, not throw.");
        Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.SelectingPredefined));
    }

    [Test]
    public void SelectPredefined_OutOfRange_IsNoOp()
    {
        BehaviorAssignmentPicker picker = new(BehaviorSubjectKind.Trigger);

        Assert.That(picker.SelectPredefined(5), Is.False);
        Assert.That(picker.SelectPredefined(-1), Is.False);
        Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.SelectingPredefined));
    }

    [Test]
    public void SelectPredefined_SingleParameterPredefined_OneCommitCompletes()
    {
        BehaviorAssignmentPicker picker = new(BehaviorSubjectKind.Trigger);

        Assert.That(picker.SelectPredefined(0), Is.True);
        Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.EditingParameter));
        Assert.That(picker.CurrentParameter!.Name, Is.EqualTo("amount"));
        Assert.That(picker.CurrentParameterPendingValue, Is.EqualTo(20), "seeded from the descriptor default.");

        Assert.That(picker.CommitCurrentParameter(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.Complete));
            Assert.That(picker.Result!.IsPredefined, Is.True);
            Assert.That(picker.Result!.PredefinedId, Is.EqualTo("healOnEnter"));
            Assert.That(picker.Result!.Parameters["amount"], Is.EqualTo(20.0));
        });
    }

    [Test]
    [Description("Pins the S3 gamepad walk end to end through the picker: patrol on an Object, speed stepped 24 -> 40 over four presses, range left at its default, both parameters land in the returned binding.")]
    public void SelectPredefined_Patrol_StepSpeedToForty_LeaveRangeDefault_ProducesExpectedBinding()
    {
        BehaviorAssignmentPicker picker = new(BehaviorSubjectKind.Object);
        int patrolIndex = picker.ApplicablePredefineds.ToList().FindIndex(d => d.Id == "patrol");

        Assert.That(picker.SelectPredefined(patrolIndex), Is.True);
        Assert.That(picker.CurrentParameter!.Name, Is.EqualTo("speed"));

        for (int i = 0; i < 4; i++)
            Assert.That(picker.AdjustCurrentParameter(+1), Is.True);
        Assert.That(picker.CurrentParameterPendingValue, Is.EqualTo(40));

        Assert.That(picker.CommitCurrentParameter(), Is.True);
        Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.EditingParameter), "patrol has a second parameter -- range.");
        Assert.That(picker.CurrentParameter!.Name, Is.EqualTo("range"));
        Assert.That(picker.CurrentParameterPendingValue, Is.EqualTo(48));

        Assert.That(picker.CommitCurrentParameter(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.Complete));
            Assert.That(picker.Result!.PredefinedId, Is.EqualTo("patrol"));
            Assert.That(picker.Result!.Parameters["speed"], Is.EqualTo(40.0));
            Assert.That(picker.Result!.Parameters["range"], Is.EqualTo(48.0));
        });
    }

    [Test]
    public void AdjustAndCommit_OutsideEditingParameterStage_AreNoOps()
    {
        BehaviorAssignmentPicker picker = new(BehaviorSubjectKind.Object);

        Assert.Multiple(() =>
        {
            Assert.That(picker.AdjustCurrentParameter(+1), Is.False, "still SelectingPredefined -- nothing to adjust.");
            Assert.That(picker.CommitCurrentParameter(), Is.False);
        });
    }

    [Test]
    public void Cancel_FromEditingParameter_IsTerminal_AndFurtherCallsNoOp()
    {
        BehaviorAssignmentPicker picker = new(BehaviorSubjectKind.Trigger);
        picker.SelectPredefined(0);

        picker.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.Cancelled));
            Assert.That(picker.Result, Is.Null);
            Assert.That(picker.AdjustCurrentParameter(+1), Is.False);
            Assert.That(picker.CommitCurrentParameter(), Is.False);
            Assert.That(picker.SelectPredefined(0), Is.False);
        });
    }
}
