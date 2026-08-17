using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Covers the DiVoid #7863 (behavior system Phase 2) predefined library additions:
/// <see cref="PredefinedBehaviors.Patrol"/> (the demo moving platform) and
/// <see cref="PredefinedBehaviors.BumpOnHitFromBelow"/> (the demo jump-block), both end-to-end through
/// <see cref="BehaviorLoader.CompileBinding"/> + <see cref="BehaviorScheduler"/> dispatch — same pattern as
/// <see cref="PredefinedBehaviorsTests"/>, proving the generated Pooscript actually parses and produces the
/// intents the glue expects.
/// </summary>
[TestFixture]
public sealed class ObjectPredefinedBehaviorsTests
{
    [Test]
    public void Patrol_OnSpawn_SeedsDirectionAndOrigin()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:0", "object", "platform");
        subject.Position = new BehaviorVector2(10, 0);
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol);

        var compiled = ctx.CompileResolved(subject, binding);
        Assert.That(compiled.IsQuarantined, Is.False);

        Assert.That(ctx.Scheduler.DispatchSpawn("object:0"), Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[]
        {
            new SetStateIntent("object:0", "dir", 1),
            new SetStateIntent("object:0", "origin", 10.0),
        }));
    }

    [Test]
    public void Patrol_OnUpdate_MovesForward_WithoutFlipping_BeforeReachingRange()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:0", "object", "platform");
        subject.Position = new BehaviorVector2(0, 0);
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol,
            new Dictionary<string, object?> { ["speed"] = 100, ["range"] = 50 });
        ctx.CompileResolved(subject, binding);
        subject.SeedState("dir", 1);
        subject.SeedState("origin", 0.0);

        Assert.That(ctx.Scheduler.DispatchUpdate("object:0", 1.0), Is.True);

        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[]
        {
            new MoveByIntent("object:0", 100.0, 0),
        }));
    }

    [Test]
    public void Patrol_OnUpdate_FlipsDirection_AfterExceedingRange()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:0", "object", "platform");
        subject.Position = new BehaviorVector2(60, 0);
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol,
            new Dictionary<string, object?> { ["speed"] = 100, ["range"] = 50 });
        ctx.CompileResolved(subject, binding);
        subject.SeedState("dir", 1);
        subject.SeedState("origin", 0.0);

        Assert.That(ctx.Scheduler.DispatchUpdate("object:0", 1.0), Is.True);

        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[]
        {
            new MoveByIntent("object:0", 100.0, 0),
            new SetStateIntent("object:0", "dir", -1),
        }));
    }

    [Test]
    public void BumpOnHitFromBelow_OnContact_StartsBump_WhenPlayerMovingUpward()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:1", "object", "jump-block");
        ctx.Player.Velocity = new BehaviorVector2(0, -50);
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.BumpOnHitFromBelow);
        ctx.CompileResolved(subject, binding);

        Assert.That(ctx.Scheduler.DispatchContact("object:1", new EventParty("player", string.Empty, new GridCell(0, 0))), Is.True);

        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[]
        {
            new SetStateIntent("object:1", "bumping", true),
            new SetStateIntent("object:1", "bumpFrames", 12),
        }));
    }

    [Test]
    public void BumpOnHitFromBelow_OnContact_DoesNothing_WhenPlayerNotMovingUpward()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:1", "object", "jump-block");
        ctx.Player.Velocity = new BehaviorVector2(0, 50);
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.BumpOnHitFromBelow);
        ctx.CompileResolved(subject, binding);

        Assert.That(ctx.Scheduler.DispatchContact("object:1", new EventParty("player", string.Empty, new GridCell(0, 0))), Is.True);

        Assert.That(ctx.Intents.Drain(), Is.Empty);
    }

    [Test]
    public void BumpOnHitFromBelow_OnContact_IgnoresRetrigger_WhileAlreadyBumping()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:1", "object", "jump-block");
        ctx.Player.Velocity = new BehaviorVector2(0, -50);
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.BumpOnHitFromBelow);
        ctx.CompileResolved(subject, binding);
        subject.SeedState("bumping", true);
        subject.SeedState("bumpFrames", 8);

        Assert.That(ctx.Scheduler.DispatchContact("object:1", new EventParty("player", string.Empty, new GridCell(0, 0))), Is.True);

        Assert.That(ctx.Intents.Drain(), Is.Empty);
    }

    [Test]
    public void BumpOnHitFromBelow_OnUpdate_RisesThenSettles_OverFixedFrameCount()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:1", "object", "jump-block");
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.BumpOnHitFromBelow,
            new Dictionary<string, object?> { ["rise"] = 6 });
        ctx.CompileResolved(subject, binding);

        subject.SeedState("bumping", true);
        subject.SeedState("bumpFrames", 12);
        Assert.That(ctx.Scheduler.DispatchUpdate("object:1", 1.0 / 60), Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[]
        {
            new MoveByIntent("object:1", 0, -6.0),
            new SetStateIntent("object:1", "bumpFrames", 11),
        }));

        subject.SeedState("bumping", true);
        subject.SeedState("bumpFrames", 1);
        Assert.That(ctx.Scheduler.DispatchUpdate("object:1", 1.0 / 60), Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[]
        {
            new MoveByIntent("object:1", 0, 6.0),
            new SetStateIntent("object:1", "bumpFrames", 0),
            new SetStateIntent("object:1", "bumping", false),
        }));
    }

    [Test]
    public void BumpOnHitFromBelow_OnUpdate_DoesNothing_WhenNotBumping()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:1", "object", "jump-block");
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.BumpOnHitFromBelow);
        ctx.CompileResolved(subject, binding);

        Assert.That(ctx.Scheduler.DispatchUpdate("object:1", 1.0 / 60), Is.True);

        Assert.That(ctx.Intents.Drain(), Is.Empty);
    }
}
