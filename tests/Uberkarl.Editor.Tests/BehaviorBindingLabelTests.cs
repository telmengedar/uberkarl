using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers <see cref="BehaviorBindingLabel"/>: the predefined/script label split, id matching, bounding, and the unbounded full-with-parameters form.</summary>
[TestFixture]
public sealed class BehaviorBindingLabelTests
{
    [Test]
    public void Format_PredefinedBinding_ReturnsTheDescriptorLabel_NotTheId()
    {
        BehaviorBinding binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol);

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("Patrol"));
    }

    [Test]
    [Description("Pins MaxLength against the longest label the current predefined library actually produces (22 characters) -- a bound set too low would clip real content, not just outliers.")]
    public void Format_PredefinedBinding_LongestRealLabel_IsNotTruncated()
    {
        BehaviorBinding binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.BumpOnHitFromBelow);

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("Bump on Hit From Below"));
    }

    [Test]
    public void Format_PredefinedBinding_UnknownId_FallsBackToTheRawId()
    {
        BehaviorBinding binding = BehaviorBinding.FromPredefined("totallyUnknownId");

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("totallyUnknownId"));
    }

    [Test]
    [Description("QA #8834 W-2: an id differing from a real predefined only by case must NOT resolve -- pins ordinal (not OrdinalIgnoreCase) id matching.")]
    public void Format_PredefinedBinding_IdDiffersOnlyByCase_FallsBackToTheRawId()
    {
        BehaviorBinding binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact.ToLowerInvariant());

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("hurtoncontact"));
    }

    [Test]
    public void Format_ScriptBinding_ConventionalPath_ReturnsTheSlug()
    {
        BehaviorBinding binding = BehaviorBinding.FromScript(ResourceReference.ToSelf(ResourcePath.Create("scripts/doorway.poo")));

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("doorway"));
    }

    [Test]
    public void Format_ScriptBinding_NonConventionalPath_ReturnsTheRawPath()
    {
        BehaviorBinding binding = BehaviorBinding.FromScript(ResourceReference.ToSelf(ResourcePath.Create("assets/notes.txt")));

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("assets/notes.txt"));
    }

    [Test]
    [Description("Exactly at MaxLength (24 chars) must not truncate -- pins the <= boundary against an off-by-one that would clip it.")]
    public void Format_LabelExactlyAtMaxLength_IsNotTruncated()
    {
        BehaviorBinding binding = BehaviorBinding.FromPredefined("abcdefghijklmnopqrstuvwx");

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("abcdefghijklmnopqrstuvwx"));
    }

    [Test]
    [Description("One character past MaxLength must truncate to 23 characters plus an ellipsis -- pins the other side of the <= boundary.")]
    public void Format_LabelOneOverMaxLength_TruncatesWithEllipsis()
    {
        BehaviorBinding binding = BehaviorBinding.FromPredefined("abcdefghijklmnopqrstuvwxy");

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("abcdefghijklmnopqrstuvw…"));
    }

    [Test]
    public void Format_ScriptBinding_LongSlug_IsBoundedTheSameWay()
    {
        BehaviorBinding binding = BehaviorBinding.FromScript(
            ResourceReference.ToSelf(ResourcePath.Create("scripts/the-doorway-that-leads-to-the-secret-room.poo")));

        Assert.That(BehaviorBindingLabel.Format(binding), Is.EqualTo("the-doorway-that-leads-…"));
    }

    [Test]
    public void Format_NullBinding_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BehaviorBindingLabel.Format((BehaviorBinding)null!));
    }

    [Test]
    public void Format_TileBehaviorOverride_Removed_ReturnsTheRemovedMarker_NotTheAbsentBinding()
    {
        TileBehaviorOverride entry = new() { Layer = 0, Cell = new GridPosition(3, 4), Removed = true };

        Assert.That(BehaviorBindingLabel.Format(entry), Is.EqualTo("no behavior"));
    }

    [Test]
    public void Format_TileBehaviorOverride_Bound_DelegatesToTheBindingFormatter()
    {
        TileBehaviorOverride entry = new() {
            Layer = 0, Cell = new GridPosition(3, 4),
            Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact),
        };

        Assert.That(BehaviorBindingLabel.Format(entry), Is.EqualTo("Hurt on Contact"));
    }

    [Test]
    public void Format_NullTileBehaviorOverride_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BehaviorBindingLabel.Format((TileBehaviorOverride)null!));
    }

    [Test]
    [Description("QA #8834 W-1: an entry that is structurally invalid (both Removed and Binding set) is not construction-enforced -- Removed must win over a conflicting Binding.")]
    public void Format_TileBehaviorOverride_RemovedAndBindingBothSet_RemovedWins()
    {
        TileBehaviorOverride entry = new() {
            Layer = 0, Cell = new GridPosition(3, 4), Removed = true,
            Binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol),
        };

        Assert.That(BehaviorBindingLabel.Format(entry), Is.EqualTo("no behavior"));
    }

    [Test]
    [Description("QA #8834 W-1: an entry with neither Removed nor Binding set is the other structurally-invalid shape -- pins that it degrades to the removed marker rather than throwing.")]
    public void Format_TileBehaviorOverride_NeitherRemovedNorBound_ReturnsTheRemovedMarker()
    {
        TileBehaviorOverride entry = new() { Layer = 0, Cell = new GridPosition(3, 4) };

        Assert.That(BehaviorBindingLabel.Format(entry), Is.EqualTo("no behavior"));
    }

    [Test]
    public void FormatFull_PredefinedBinding_NoParameters_ReturnsTheLabelOnly()
    {
        BehaviorBinding binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol);

        Assert.That(BehaviorBindingLabel.FormatFull(binding), Is.EqualTo("Patrol"));
    }

    [Test]
    public void FormatFull_PredefinedBinding_WithParameters_AppendsThemUnbounded()
    {
        BehaviorBinding binding = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol,
            new Dictionary<string, object?> { ["speed"] = 40d, ["range"] = 48d });

        Assert.That(BehaviorBindingLabel.FormatFull(binding), Is.EqualTo("Patrol (speed 40, range 48)"));
    }

    [Test]
    [Description("QA #8826 W-8 / design ruling 2026-08-21: a parameter-only reassignment (e.g. speed 24 -> 40) is invisible to Format, so FormatFull must be the formatter that makes it visible.")]
    public void FormatFull_PredefinedBinding_ParameterOnlyChange_IsVisible_UnlikeTheBoundedFormat()
    {
        BehaviorBinding original = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol,
            new Dictionary<string, object?> { ["speed"] = 24d, ["range"] = 48d });
        BehaviorBinding reassigned = BehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol,
            new Dictionary<string, object?> { ["speed"] = 40d, ["range"] = 48d });

        Assert.That(BehaviorBindingLabel.FormatFull(reassigned), Is.Not.EqualTo(BehaviorBindingLabel.FormatFull(original)));
        Assert.That(BehaviorBindingLabel.Format(reassigned), Is.EqualTo(BehaviorBindingLabel.Format(original)));
    }

    [Test]
    public void FormatFull_ScriptBinding_LongSlug_IsNotTruncated()
    {
        BehaviorBinding binding = BehaviorBinding.FromScript(
            ResourceReference.ToSelf(ResourcePath.Create("scripts/the-doorway-that-leads-to-the-secret-room.poo")));

        Assert.That(BehaviorBindingLabel.FormatFull(binding), Is.EqualTo("the-doorway-that-leads-to-the-secret-room"));
    }

    [Test]
    public void FormatFull_TileBehaviorOverride_Removed_ReturnsTheRemovedMarker()
    {
        TileBehaviorOverride entry = new() { Layer = 0, Cell = new GridPosition(3, 4), Removed = true };

        Assert.That(BehaviorBindingLabel.FormatFull(entry), Is.EqualTo("no behavior"));
    }

    [Test]
    public void FormatFull_NullBinding_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BehaviorBindingLabel.FormatFull((BehaviorBinding)null!));
    }

    [Test]
    public void FormatFull_NullTileBehaviorOverride_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BehaviorBindingLabel.FormatFull((TileBehaviorOverride)null!));
    }
}
