using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Editor.Input;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers M5a's extension of <see cref="BehaviorAssignmentPicker"/>: the combined <see cref="BehaviorAssignmentPicker.Choices"/> list, <see cref="BehaviorAssignmentPicker.SelectChoice"/>'s dispatch, and <see cref="BehaviorAssignmentPicker.CreateNewScript"/>'s slug allocation.</summary>
[TestFixture]
public sealed class BehaviorAssignmentPickerScriptTests
{
    private static readonly ResourcePath DoorOpener = ResourcePath.Create("scripts/door-opener.poo");
    private static readonly ResourcePath PatrolFast = ResourcePath.Create("scripts/patrol-fast.poo");
    private static readonly ResourcePath ScriptAaa = ResourcePath.Create("scripts/aaa.poo");
    private static readonly ResourcePath ScriptBbb = ResourcePath.Create("scripts/bbb.poo");
    private static readonly ResourcePath ScriptCcc = ResourcePath.Create("scripts/ccc.poo");
    private static readonly ResourcePath ScriptDdd = ResourcePath.Create("scripts/ddd.poo");
    private static readonly ResourcePath ScriptEee = ResourcePath.Create("scripts/eee.poo");

    [Test]
    [Description("The new-script row must sit at exactly ApplicablePredefineds.Count when no scripts exist yet, never off by one.")]
    public void Choices_ForObjectWithNoScripts_IsPredefinedsThenNewScriptRow_InOrder()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Choices, Has.Count.EqualTo(4));
            Assert.That(picker.Choices[0].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.Predefined));
            Assert.That(picker.Choices[1].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.Predefined));
            Assert.That(picker.Choices[2].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.Predefined));
            Assert.That(picker.Choices[3].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.NewScript));
        });
    }

    [Test]
    [Description("Existing scripts must sit strictly between the predefined segment and the trailing new-script row, at literal indices.")]
    public void Choices_WithExistingScripts_InsertsThemBetweenPredefinedsAndNewScriptRow()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object, new[] { DoorOpener, PatrolFast });

        Assert.Multiple(() =>
        {
            Assert.That(picker.Choices, Has.Count.EqualTo(6));
            Assert.That(picker.Choices[3].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.ExistingScript));
            Assert.That(picker.Choices[3].Label, Is.EqualTo("door-opener"));
            Assert.That(picker.Choices[4].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.ExistingScript));
            Assert.That(picker.Choices[4].Label, Is.EqualTo("patrol-fast"));
            Assert.That(picker.Choices[5].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.NewScript));
        });
    }

    [Test]
    [Description("DiVoid #8760 §E: the level-script picker's list was always empty pre-M5, leaving AssignLevelScript unreachable; the new-script row makes it non-empty for the first time.")]
    public void Choices_ForLevelScript_WithNoScripts_IsJustTheNewScriptRow()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.LevelScript);

        Assert.That(picker.Choices, Has.Count.EqualTo(1));
        Assert.That(picker.Choices[0].Kind, Is.EqualTo(BehaviorAssignmentChoiceKind.NewScript));
    }

    [Test]
    public void SelectChoice_ExistingScriptRow_CompletesImmediately_MintingNothing()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object, new[] { DoorOpener });

        var accepted = picker.SelectChoice(3);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.Complete));
            Assert.That(picker.Result!.IsScript, Is.True);
            Assert.That(picker.Result!.Script!.Value.Path, Is.EqualTo(DoorOpener));
            Assert.That(picker.Result!.Script!.Value.IsSelf, Is.True);
            Assert.That(picker.MintedScriptPath, Is.Null, "sharing an existing script must write nothing new to the table.");
            Assert.That(picker.MintedScriptSource, Is.Null);
        });
    }

    [Test]
    public void SelectChoice_NewScriptRow_TransitionsToNamingStage_WithoutCompletingOrMinting()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);
        int newScriptRowIndex = picker.Choices.Count - 1;

        var accepted = picker.SelectChoice(newScriptRowIndex);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.NamingNewScript));
            Assert.That(picker.Result, Is.Null);
            Assert.That(picker.MintedScriptPath, Is.Null);
        });
    }

    [Test]
    [Description("Five existing scripts (more than ApplicablePredefineds.Count) so a broken index offset lands on a valid-but-wrong neighbour, provably by VALUE -- with only two scripts (DiVoid #8786 e) every wrong index falls out of range, so the offset mutant can only ever die by ArgumentOutOfRangeException, never by this assertion actually checking the resolved path.")]
    public void SelectChoice_SecondExistingScriptRow_ResolvesTheCorrectPath_NotItsNeighbour()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object, new[] { ScriptAaa, ScriptBbb, ScriptCcc, ScriptDdd, ScriptEee });

        Assert.That(picker.SelectChoice(4), Is.True);

        Assert.That(picker.Result!.Script!.Value.Path, Is.EqualTo(ScriptBbb));
    }

    [Test]
    [Description("LevelScript has zero applicable predefineds, so the offset subtracted in SelectChoice is already 0 on this axis -- an offset bug here would be invisible to a mutation kill (offset and no-offset compute the same index), but the resolution itself must still be correct end-to-end (DiVoid #8786 e, 'also untested').")]
    public void SelectChoice_LevelScriptExistingScriptRow_ResolvesTheCorrectPath()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.LevelScript, new[] { DoorOpener, PatrolFast });
        Assert.That(picker.ApplicablePredefineds, Is.Empty);

        Assert.That(picker.SelectChoice(1), Is.True);

        Assert.That(picker.Result!.Script!.Value.Path, Is.EqualTo(PatrolFast));
    }

    [Test]
    public void SelectChoice_PredefinedRow_BehavesLikeSelectPredefined()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Trigger);

        var accepted = picker.SelectChoice(0);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.EditingParameter));
            Assert.That(picker.CurrentParameter!.Name, Is.EqualTo("amount"));
        });
    }

    [TestCase(-1)]
    [TestCase(4)]
    public void SelectChoice_OutOfRange_IsNoOp(int index)
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);

        Assert.That(picker.SelectChoice(index), Is.False);
        Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.SelectingPredefined));
    }

    [Test]
    public void CreateNewScript_AllocatesPathFromName_AndCompletes()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);
        picker.SelectChoice(picker.Choices.Count - 1);

        var accepted = picker.CreateNewScript("Door Opener", _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.Complete));
            Assert.That(picker.MintedScriptPath, Is.EqualTo(DoorOpener));
            Assert.That(picker.MintedScriptSource, Is.Not.Null.And.Not.Empty);
            Assert.That(picker.Result!.IsScript, Is.True);
            Assert.That(picker.Result!.Script!.Value.Path, Is.EqualTo(DoorOpener));
        });
    }

    [Test]
    [Description("Two authors naming a script the same thing must land on different slugs.")]
    public void CreateNewScript_NameCollidesWithExistingSlug_AppendsDashTwo()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);
        picker.SelectChoice(picker.Choices.Count - 1);

        var accepted = picker.CreateNewScript("Door Opener", slug => slug == "door-opener");

        Assert.That(accepted, Is.True);
        Assert.That(picker.MintedScriptPath, Is.EqualTo(ResourcePath.Create("scripts/door-opener-2.poo")));
    }

    [Test]
    [Description("A name that slugs to empty must still allocate, via ScriptResourcePaths' own 'script' fallback.")]
    public void CreateNewScript_NameWithNoAlphanumericContent_UsesScriptFallback()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);
        picker.SelectChoice(picker.Choices.Count - 1);

        var accepted = picker.CreateNewScript("###", _ => false);

        Assert.That(accepted, Is.True);
        Assert.That(picker.MintedScriptPath, Is.EqualTo(ResourcePath.Create("scripts/script.poo")));
    }

    [TestCase("")]
    [TestCase("   ")]
    [Description("A blank commit is a no-op, not a cancel, so the flow can retry from the same still-open list.")]
    public void CreateNewScript_BlankName_IsNoOp_StageStaysNamingNewScript(string blankName)
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);
        picker.SelectChoice(picker.Choices.Count - 1);

        var accepted = picker.CreateNewScript(blankName, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.NamingNewScript));
            Assert.That(picker.Result, Is.Null);
            Assert.That(picker.MintedScriptPath, Is.Null);
        });
    }

    [Test]
    [Description("DiVoid #8786 CF: cancelling the naming keyboard must return the picker to a state where the still-open choice list can select again, not leave it stuck.")]
    public void CancelNewScriptNaming_FromNamingStage_ReturnsToSelectingPredefined_AndListSelectsAgain()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Trigger);
        picker.SelectChoice(picker.Choices.Count - 1);
        Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.NamingNewScript));

        var accepted = picker.CancelNewScriptNaming();

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.SelectingPredefined));
            Assert.That(picker.Result, Is.Null);
        });

        Assert.That(picker.SelectChoice(0), Is.True);
        Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.EditingParameter));
    }

    [Test]
    public void CancelNewScriptNaming_OutsideNamingStage_IsNoOp()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);

        var accepted = picker.CancelNewScriptNaming();

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.SelectingPredefined));
        });
    }

    [Test]
    public void CreateNewScript_OutsideNamingStage_IsNoOp()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);

        var accepted = picker.CreateNewScript("Door Opener", _ => false);

        Assert.That(accepted, Is.False);
        Assert.That(picker.Stage, Is.EqualTo(BehaviorAssignmentStage.SelectingPredefined));
    }

    [Test]
    public void CreateNewScript_NullPredicate_Throws()
    {
        var picker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);
        picker.SelectChoice(picker.Choices.Count - 1);

        Assert.That(() => picker.CreateNewScript("Door Opener", null!), Throws.ArgumentNullException);
    }

    [Test]
    [Description("Pins that the picker seeds some per-kind text, not the specific handler -- that belongs to BehaviorScriptTemplatesTests.")]
    public void CreateNewScript_MintsTheSubjectKindsOwnTemplate_NotAnotherKinds()
    {
        var tilePicker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Tile);
        tilePicker.SelectChoice(tilePicker.Choices.Count - 1);
        tilePicker.CreateNewScript("Spike", _ => false);

        var objectPicker = new BehaviorAssignmentPicker(BehaviorSubjectKind.Object);
        objectPicker.SelectChoice(objectPicker.Choices.Count - 1);
        objectPicker.CreateNewScript("Mover", _ => false);

        Assert.That(tilePicker.MintedScriptSource, Is.Not.EqualTo(objectPicker.MintedScriptSource));
    }
}
