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
}
