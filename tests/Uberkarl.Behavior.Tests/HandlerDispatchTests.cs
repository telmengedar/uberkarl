using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// End-to-end, real-Pooscript coverage of the P0 acceptance gate's first requirement (DiVoid #7710): "a
/// well-behaved script: its onContact/onUpdate/etc. handlers fire on the right events and produce the
/// correct intents." Exercises the full path: <see cref="BehaviorLoader"/> parses + runs the one-time init,
/// <see cref="BehaviorScheduler"/> dispatches, the reference facades record intents.
/// </summary>
[TestFixture]
public sealed class HandlerDispatchTests
{
    [Test]
    public void OnContact_And_OnUpdate_FireOnRightEvents_AndProduceCorrectIntents()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");
        const string source = """
            $onContact = $other => { player.hurt(10); self.setState("hit", true); }
            $onUpdate = $delta => { self.moveBy(delta * 2, 0); }
            { "onContact": onContact, "onUpdate": onUpdate }
            """;

        var instance = ctx.Compile(subject, source);
        Assert.That(instance.IsQuarantined, Is.False);

        var contactFired = ctx.Scheduler.DispatchContact("spike-1", new EventParty("player", string.Empty, new GridCell(1, 1)));

        Assert.That(contactFired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[]
        {
            new HurtIntent(BehaviorSubjectIds.Player, 10),
            new SetStateIntent("spike-1", "hit", true),
        }));

        var updateFired = ctx.Scheduler.DispatchUpdate("spike-1", 0.5);

        Assert.That(updateFired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new MoveByIntent("spike-1", 1.0, 0) }));
    }

    [Test]
    public void Dispatch_ForEventTheScriptDoesNotImplement_IsANoOp()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");
        var instance = ctx.Compile(subject, """
            $onContact = $other => { self.moveBy(1, 0); }
            { "onContact": onContact }
            """);

        Assert.That(instance.IsQuarantined, Is.False);

        var fired = ctx.Scheduler.DispatchUpdate("spike-1", 0.1);

        Assert.That(fired, Is.False);
        Assert.That(ctx.Intents.Intents, Is.Empty);
    }

    [Test]
    public void OnEnter_And_OnLeave_ReadTheEventFacade_AndAreEdgeDistinct()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("trigger-1", "trigger", string.Empty);
        const string source = """
            $onEnter = $who => { level.setState("visitor", event.other.name); }
            $onLeave = $who => { level.setState("visitor", "") }
            { "onEnter": onEnter, "onLeave": onLeave }
            """;
        ctx.Compile(subject, source);

        var enterFired = ctx.Scheduler.DispatchEnter("trigger-1", new EventParty("player", "hero", new GridCell(2, 2)));

        Assert.That(enterFired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent(BehaviorSubjectIds.Level, "visitor", "hero") }));

        var leaveFired = ctx.Scheduler.DispatchLeave("trigger-1", new EventParty("player", "hero", new GridCell(2, 3)));

        Assert.That(leaveFired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent(BehaviorSubjectIds.Level, "visitor", "") }));
    }

    [Test]
    public void SelfMoveTo_AcceptsAGridCellDictionaryLiteral()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "platform");
        const string source = """
            $onSpawn = [] => { self.moveTo({ "X": 5, "Y": 2 }) }
            { "onSpawn": onSpawn }
            """;
        ctx.Compile(subject, source);

        var fired = ctx.Scheduler.DispatchSpawn("obj-1");

        Assert.That(fired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new MoveToCellIntent("obj-1", new GridCell(5, 2)) }));
    }

    [Test]
    public void CrossEntityScripting_LevelObject_MovesAnotherSubject()
    {
        var ctx = new BehaviorTestContext();
        var gate = ctx.CreateSubject("gate-1", "object", "gate");
        ctx.Level.Objects["gate"] = gate;

        var lever = ctx.CreateSubject("lever-1", "object", "lever");
        const string source = """
            $onContact = $other => { level.object("gate").setState("open", true); }
            { "onContact": onContact }
            """;
        ctx.Compile(lever, source);

        var fired = ctx.Scheduler.DispatchContact("lever-1", new EventParty("player", string.Empty, default));

        Assert.That(fired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent("gate-1", "open", true) }));
    }
}
