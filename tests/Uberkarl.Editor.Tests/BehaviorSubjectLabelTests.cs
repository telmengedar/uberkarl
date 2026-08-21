using NUnit.Framework;
using Uberkarl.Behavior;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers <see cref="BehaviorSubjectLabel"/>: the kind/name combination, the unnamed case, and the name bound.</summary>
[TestFixture]
public sealed class BehaviorSubjectLabelTests
{
    [Test]
    public void Format_Object_WithName_ReturnsKindAndQuotedName()
    {
        Assert.That(BehaviorSubjectLabel.Format(BehaviorSubjectKind.Object, "jump-block-1"), Is.EqualTo("Object 'jump-block-1'"));
    }

    [Test]
    public void Format_Trigger_WithName_ReturnsKindAndQuotedName()
    {
        Assert.That(BehaviorSubjectLabel.Format(BehaviorSubjectKind.Trigger, "heal-zone"), Is.EqualTo("Trigger 'heal-zone'"));
    }

    [Test]
    public void Format_Tile_ReturnsKindAlone_NameArgumentIgnored()
    {
        Assert.That(BehaviorSubjectLabel.Format(BehaviorSubjectKind.Tile, "should-not-appear"), Is.EqualTo("Tile"));
    }

    [Test]
    public void Format_LevelScript_ReturnsTheTwoWordKindLabel()
    {
        Assert.That(BehaviorSubjectLabel.Format(BehaviorSubjectKind.LevelScript, null), Is.EqualTo("Level Script"));
    }

    [Test]
    [Description("An object placed without a Placement.Name must read as the bare kind -- not \"Object ''\" with empty, misleading quotes.")]
    public void Format_Object_NullName_ReturnsKindAlone_NoEmptyQuotes()
    {
        Assert.That(BehaviorSubjectLabel.Format(BehaviorSubjectKind.Object, null), Is.EqualTo("Object"));
    }

    [Test]
    [Description("An empty (not null) name is the same honest-gap case as null -- both guard on IsNullOrEmpty upstream.")]
    public void Format_Trigger_EmptyName_ReturnsKindAlone_NoEmptyQuotes()
    {
        Assert.That(BehaviorSubjectLabel.Format(BehaviorSubjectKind.Trigger, string.Empty), Is.EqualTo("Trigger"));
    }

    [Test]
    [Description("Exactly at MaxNameLength (32 chars) must not truncate -- pins the <= boundary against an off-by-one that would clip it.")]
    public void Format_NameExactlyAtMaxLength_IsNotTruncated()
    {
        const string name = "abcdefghijklmnopqrstuvwxyz012345";

        Assert.That(BehaviorSubjectLabel.Format(BehaviorSubjectKind.Object, name), Is.EqualTo("Object 'abcdefghijklmnopqrstuvwxyz012345'"));
    }

    [Test]
    [Description("One character past MaxNameLength must truncate to 31 characters plus an ellipsis -- pins the other side of the <= boundary.")]
    public void Format_NameOneOverMaxLength_TruncatesWithEllipsis()
    {
        const string name = "abcdefghijklmnopqrstuvwxyz0123456";

        Assert.That(BehaviorSubjectLabel.Format(BehaviorSubjectKind.Object, name),
            Is.EqualTo("Object 'abcdefghijklmnopqrstuvwxyz01234…'"));
    }
}
