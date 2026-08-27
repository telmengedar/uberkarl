using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>Covers <see cref="BehaviorSourceValidator"/> — the editor-side parse/compile check that runs <see cref="BehaviorLoader.Compile"/>, the runtime's own call, against detached facades.</summary>
[TestFixture]
public sealed class BehaviorSourceValidatorTests
{
    private const string CompilingSource = """
        $onUpdate = [] => { self.setState("ok", true); }
        { "onUpdate": onUpdate }
        """;

    [Test]
    public void Validate_CompilingSource_ReturnsNull()
    {
        var reason = BehaviorSourceValidator.Validate(CompilingSource, BehaviorScriptRole.Behavior);

        Assert.That(reason, Is.Null);
    }

    [Test]
    public void Validate_UnbalancedBrace_ReturnsAParseErrorReason()
    {
        var reason = BehaviorSourceValidator.Validate("$onContact = $other => { ", BehaviorScriptRole.Behavior);

        Assert.That(reason, Does.Contain("parse error"));
    }

    [Test]
    public void Validate_EmptySource_ReturnsTheEndedWithNothingReason()
    {
        var reason = BehaviorSourceValidator.Validate(string.Empty, BehaviorScriptRole.Behavior);

        Assert.That(reason, Does.Contain("ended with nothing"));
    }

    [Test]
    public void Validate_InitResultNotAMap_ReturnsTheHandlerMapReason()
    {
        var reason = BehaviorSourceValidator.Validate("42", BehaviorScriptRole.Behavior);

        Assert.That(reason, Does.Contain("map of handler lambdas"));
    }

    [Test]
    [Description("Role selection actually changes which ScriptLimits gets used: ten levels of eager init-time recursion clears the Behavior role's MaxDepth (8) but not the Init role's (12) -- pins that Validate is not silently ignoring its role parameter.")]
    public void Validate_InitTimeRecursionBetweenTheTwoDepthCaps_QuarantinesUnderBehaviorRole_ButNotUnderInitRole()
    {
        const string DeepInitSource = """
            $recurse = $n => { $n < 10 ? recurse(n + 1) : n }
            $depth = recurse(0)
            $onUpdate = [] => { self.setState("ok", true); }
            { "onUpdate": onUpdate }
            """;

        var behaviorReason = BehaviorSourceValidator.Validate(DeepInitSource, BehaviorScriptRole.Behavior);
        var initReason = BehaviorSourceValidator.Validate(DeepInitSource, BehaviorScriptRole.Init);

        Assert.Multiple(() =>
        {
            Assert.That(behaviorReason, Is.Not.Null, "ten levels of init-time recursion must exceed the Behavior role's tighter MaxDepth (8)");
            Assert.That(initReason, Is.Null, "the same recursion must stay under the Init role's raised MaxDepth (12)");
        });
    }

    [TestCase(BehaviorSubjectKind.Tile)]
    [TestCase(BehaviorSubjectKind.Trigger)]
    [TestCase(BehaviorSubjectKind.Object)]
    public void Validate_FreshStarterTemplate_UnderBehaviorRole_CompilesWithoutAQuarantineReason(BehaviorSubjectKind kind)
    {
        var reason = BehaviorSourceValidator.Validate(BehaviorScriptTemplates.For(kind), BehaviorScriptRole.Behavior);

        Assert.That(reason, Is.Null);
    }

    [Test]
    public void Validate_FreshLevelScriptTemplate_UnderInitRole_CompilesWithoutAQuarantineReason()
    {
        var reason = BehaviorSourceValidator.Validate(BehaviorScriptTemplates.For(BehaviorSubjectKind.LevelScript), BehaviorScriptRole.Init);

        Assert.That(reason, Is.Null);
    }
}
