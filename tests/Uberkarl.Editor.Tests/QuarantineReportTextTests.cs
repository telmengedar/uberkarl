using System;
using NUnit.Framework;
using Uberkarl.Behavior;

namespace Uberkarl.Editor.Tests;

/// <summary>Proves <see cref="QuarantineReportText"/> renders the §4.7 report block (design docs/architecture/quarantine-visibility.md).</summary>
[TestFixture]
public sealed class QuarantineReportTextTests
{
    [Test]
    public void Format_EmptyList_ReturnsEmptyString()
    {
        Assert.That(QuarantineReportText.Format(Array.Empty<QuarantinedSubject>()), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Format_NamedObjectWithCell_RendersKindNameAndCell()
    {
        var quarantine = new QuarantinedSubject("object:2", "object", "moving-platform-1", new GridCell(50, 9), BehaviorEventKind.OnContact, "threw: boom");

        string text = QuarantineReportText.Format(new[] { quarantine });

        Assert.That(text, Is.EqualTo("Behavior stopped (1)\nObject 'moving-platform-1' (50, 9) — onContact: threw: boom"));
    }

    [Test]
    public void Format_NamedTriggerWithCell_RendersKindNameAndCell()
    {
        var quarantine = new QuarantinedSubject("trigger:0", "trigger", "heal-zone", new GridCell(30, 9), BehaviorEventKind.OnEnter, "threw: boom");

        string text = QuarantineReportText.Format(new[] { quarantine });

        Assert.That(text, Is.EqualTo("Behavior stopped (1)\nTrigger 'heal-zone' (30, 9) — onEnter: threw: boom"));
    }

    [Test]
    public void Format_UnnamedTileWithCell_RendersKindAndCellWithoutAName()
    {
        var quarantine = new QuarantinedSubject("tile:0:20:11", "tile", string.Empty, new GridCell(20, 11), null, "parse error: unexpected end of input");

        string text = QuarantineReportText.Format(new[] { quarantine });

        Assert.That(text, Is.EqualTo("Behavior stopped (1)\nTile (20, 11) — init: parse error: unexpected end of input"));
    }

    [Test]
    public void Format_LevelScript_RendersWithoutACell()
    {
        var quarantine = new QuarantinedSubject("level-script", "level", string.Empty, default, BehaviorEventKind.OnUpdate, "exceeded budget (ScriptStepLimitExceededException): step limit hit");

        string text = QuarantineReportText.Format(new[] { quarantine });

        Assert.That(text, Is.EqualTo("Behavior stopped (1)\nLevel Script — onUpdate: exceeded budget (ScriptStepLimitExceededException): step limit hit"));
    }

    [Test]
    public void Format_NullTriggeringEvent_RendersAsInit()
    {
        var quarantine = new QuarantinedSubject("object:0", "object", "buggy", new GridCell(1, 1), null, "parse error: bad");

        string text = QuarantineReportText.Format(new[] { quarantine });

        Assert.That(text, Does.Contain("— init: "));
    }

    [Test]
    public void Format_NonNullTriggeringEvent_RendersTheAuthorsHandlerSpelling_NotTheEnumsPascalCase()
    {
        var quarantine = new QuarantinedSubject("object:0", "object", "buggy", new GridCell(1, 1), BehaviorEventKind.OnUpdate, "threw: boom");

        string text = QuarantineReportText.Format(new[] { quarantine });

        Assert.That(text, Does.Contain("— onUpdate: "));
        Assert.That(text, Does.Not.Contain("OnUpdate"));
    }

    [Test]
    public void Format_MultiLineReason_RendersOnlyTheFirstLineWithNoLineBreak()
    {
        var quarantine = new QuarantinedSubject("object:0", "object", "buggy", new GridCell(1, 1), BehaviorEventKind.OnContact,
            "threw: System.Exception: boom\r\n   at Uberkarl.Behavior.Something()\n   at Uberkarl.Behavior.Other()");

        string text = QuarantineReportText.Format(new[] { quarantine });

        Assert.That(text, Is.EqualTo("Behavior stopped (1)\nObject 'buggy' (1, 1) — onContact: threw: System.Exception: boom"));
        Assert.That(text, Does.Not.Contain("\r"));
    }

    [Test]
    public void Format_HeaderCount_EqualsTheNumberOfRecords_ForOneAndForSeveral()
    {
        QuarantinedSubject[] one = { new("object:0", "object", "a", new GridCell(0, 0), null, "parse error: a") };
        QuarantinedSubject[] several =
        {
            new("object:0", "object", "a", new GridCell(0, 0), null, "parse error: a"),
            new("object:1", "object", "b", new GridCell(1, 0), BehaviorEventKind.OnUpdate, "threw: b"),
            new("object:2", "object", "c", new GridCell(2, 0), BehaviorEventKind.OnContact, "threw: c"),
        };

        Assert.That(QuarantineReportText.Format(one), Does.StartWith("Behavior stopped (1)\n"));
        Assert.That(QuarantineReportText.Format(several), Does.StartWith("Behavior stopped (3)\n"));
    }

    [Test]
    public void Format_PreservesInputOrderAsOutputOrder()
    {
        QuarantinedSubject[] quarantines =
        {
            new("object:0", "object", "first", new GridCell(0, 0), null, "parse error: a"),
            new("object:1", "object", "second", new GridCell(1, 0), null, "parse error: b"),
        };

        string text = QuarantineReportText.Format(quarantines);

        Assert.That(text.IndexOf("first", StringComparison.Ordinal), Is.LessThan(text.IndexOf("second", StringComparison.Ordinal)));
    }
}
