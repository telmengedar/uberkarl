using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Covers the DiVoid #7738 Phase-1 predefined library seed (design #7704 §5.7): <see cref="PredefinedBehaviors.HurtOnContact"/>
/// (the demo spike tile) and <see cref="PredefinedBehaviors.HealOnEnter"/> (the demo trigger), both through
/// <see cref="PredefinedBehaviors.TryGetSource"/> directly and end-to-end through
/// <see cref="BehaviorLoader.CompileBinding"/> + <see cref="BehaviorScheduler"/> dispatch, proving a predefined
/// binding produces the same real intents a hand-written script would (P0's <see cref="HandlerDispatchTests"/>
/// pattern, reused for the predefined path).
/// </summary>
[TestFixture]
public sealed class PredefinedBehaviorsTests
{
    [Test]
    public void TryGetSource_HurtOnContact_UsesDefaultAmount_WhenParameterAbsent()
    {
        var found = PredefinedBehaviors.TryGetSource(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?>(), out var source);

        Assert.That(found, Is.True);
        Assert.That(source, Does.Contain("player.hurt(10)"));
    }

    [Test]
    public void TryGetSource_HealOnEnter_UsesSuppliedAmount()
    {
        var found = PredefinedBehaviors.TryGetSource(PredefinedBehaviors.HealOnEnter, new Dictionary<string, object?> { ["amount"] = 35 }, out var source);

        Assert.That(found, Is.True);
        Assert.That(source, Does.Contain("player.heal(35)"));
    }

    [Test]
    public void TryGetSource_UnknownId_ReturnsFalse()
    {
        var found = PredefinedBehaviors.TryGetSource("not-a-real-id", new Dictionary<string, object?>(), out var source);

        Assert.That(found, Is.False);
        Assert.That(source, Is.Empty);
    }

    [Test]
    public void CompileBinding_HurtOnContact_DispatchesRealHurtIntent()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = 15 });

        var compiled = ctx.CompileResolved(subject, binding);
        Assert.That(compiled.IsQuarantined, Is.False);

        var fired = ctx.Scheduler.DispatchContact("spike-1", new EventParty("player", string.Empty, new GridCell(0, 0)));

        Assert.That(fired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new HurtIntent(BehaviorSubjectIds.Player, 15) }));
    }

    [Test]
    public void CompileBinding_HealOnEnter_DispatchesRealHealIntent()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("trigger-1", "trigger", "heal-zone");
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.HealOnEnter);

        var compiled = ctx.CompileResolved(subject, binding);
        Assert.That(compiled.IsQuarantined, Is.False);

        var fired = ctx.Scheduler.DispatchEnter("trigger-1", new EventParty("player", string.Empty, new GridCell(1, 1)));

        Assert.That(fired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new HealIntent(BehaviorSubjectIds.Player, 20) }));
    }

    [Test]
    public void CompileBinding_UnknownPredefinedId_QuarantinesInsteadOfThrowing()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "mystery");
        var binding = ResolvedBehaviorBinding.FromPredefined("not-a-real-id");

        var compiled = ctx.CompileResolved(subject, binding);

        Assert.That(compiled.IsQuarantined, Is.True);
        Assert.That(compiled.QuarantineReason, Does.Contain("unknown predefined"));
    }

    [Test]
    public void CompileBinding_NonNumericParameter_QuarantinesInsteadOfThrowing()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = "fast" });

        CompiledBehavior compiled = null;
        Assert.That(() => compiled = ctx.CompileResolved(subject, binding), Throws.Nothing,
            "a package may legally declare a string parameter, so this is bad content -- BehaviorLoader promises to always return a usable, possibly-quarantined result rather than throwing during level load");

        Assert.That(compiled.IsQuarantined, Is.True);
        Assert.That(compiled.QuarantineReason, Does.Contain("amount"), "the author needs to know which parameter is wrong");
        Assert.That(compiled.QuarantineReason, Does.Contain("fast"), "and what value was rejected");
    }

    [Test]
    public void CompileBinding_NumericParameterWrittenAsText_IsStillAccepted()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-2", "tile", "spike");
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.HurtOnContact, new Dictionary<string, object?> { ["amount"] = "15" });

        var compiled = ctx.CompileResolved(subject, binding);

        Assert.That(compiled.IsQuarantined, Is.False,
            "leniency is deliberate: JSON round-trips can hand us a numeric string, and rejecting it would break valid content");

        ctx.Scheduler.DispatchContact("spike-2", new EventParty("player", string.Empty, new GridCell(0, 0)));
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new HurtIntent(BehaviorSubjectIds.Player, 15) }));
    }
}
