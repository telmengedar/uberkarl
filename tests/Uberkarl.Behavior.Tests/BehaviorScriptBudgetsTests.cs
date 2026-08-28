using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>Pins the wall-clock deadline floors in <see cref="BehaviorScriptBudgets"/> against ordinary host jitter.</summary>
[TestFixture]
public sealed class BehaviorScriptBudgetsTests
{
    [Test]
    [Description("A behavior deadline near one physics frame quarantines healthy per-frame scripts on ordinary host jitter, not just runaway ones.")]
    public void DefaultBehavior_Timeout_ClearsOnePhysicsFrameWithRealMargin()
    {
        Assert.That(BehaviorScriptBudgets.DefaultBehavior().Timeout!.Value, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100)));
    }

    [Test]
    [Description("An init deadline near a JIT cold-start hiccup quarantines the level script on the very first level load.")]
    public void DefaultInit_Timeout_ClearsAMeasuredColdStartHiccupWithRealMargin()
    {
        Assert.That(BehaviorScriptBudgets.DefaultInit().Timeout!.Value, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500)));
    }

    [Test]
    [Description("DiVoid #10000: measured deepest parse-time nesting in tools/SampleContent + the predefined behavior library is 5 (BumpOnHitFromBelow); MaxParseDepth must clear that with real headroom while staying far below the measured ~600-deep stack-overflow ceiling (DiVoid #9999).")]
    public void DefaultBehavior_MaxParseDepth_ClearsMeasuredContentWithHeadroomBelowTheStackCeiling()
    {
        Assert.That(BehaviorScriptBudgets.DefaultBehavior().MaxParseDepth, Is.Not.Null);
        Assert.That(BehaviorScriptBudgets.DefaultBehavior().MaxParseDepth!.Value, Is.GreaterThanOrEqualTo(20));
        Assert.That(BehaviorScriptBudgets.DefaultBehavior().MaxParseDepth!.Value, Is.LessThanOrEqualTo(100));
    }

    [Test]
    [Description("DiVoid #10000: measured deepest parse-time nesting in the level script (tools/SampleContent) is 3; MaxParseDepth must clear that with real headroom while staying far below the measured ~600-deep stack-overflow ceiling (DiVoid #9999), and stay raised over DefaultBehavior() like every other init knob.")]
    public void DefaultInit_MaxParseDepth_ClearsMeasuredContentWithHeadroomBelowTheStackCeiling()
    {
        Assert.That(BehaviorScriptBudgets.DefaultInit().MaxParseDepth, Is.Not.Null);
        Assert.That(BehaviorScriptBudgets.DefaultInit().MaxParseDepth!.Value, Is.GreaterThanOrEqualTo(20));
        Assert.That(BehaviorScriptBudgets.DefaultInit().MaxParseDepth!.Value, Is.LessThanOrEqualTo(150));
        Assert.That(BehaviorScriptBudgets.DefaultInit().MaxParseDepth!.Value, Is.GreaterThan(BehaviorScriptBudgets.DefaultBehavior().MaxParseDepth!.Value));
    }
}
