using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

[TestFixture]
public sealed class BehaviorEventDirectionTests
{
    [Test]
    public void DispatchContact_WithDirection_SetsCurrentEventDirection()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("block-1", "tile", "block");
        ctx.Compile(subject, """
            $onContact = $other => { self.setState("hit", true); }
            { "onContact": onContact }
            """);

        ctx.Scheduler.DispatchContact("block-1", new EventParty("player", string.Empty, new GridCell(0, 0)), "below");

        Assert.That(ctx.Scheduler.CurrentEvent.Direction, Is.EqualTo("below"));
    }

    [Test]
    public void DispatchContact_WithoutDirection_LeavesCurrentEventDirectionNull()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("block-1", "tile", "block");
        ctx.Compile(subject, """
            $onContact = $other => { self.setState("hit", true); }
            { "onContact": onContact }
            """);

        ctx.Scheduler.DispatchContact("block-1", new EventParty("player", string.Empty, new GridCell(0, 0)));

        Assert.That(ctx.Scheduler.CurrentEvent.Direction, Is.Null);
    }

    [Test]
    public void OnContact_ScriptReadsEventDirection_SeesTheClassifiedSide()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("block-1", "tile", "block");
        ctx.Compile(subject, """
            $onContact = $other => { self.setState("side", event.direction); }
            { "onContact": onContact }
            """);

        ctx.Scheduler.DispatchContact("block-1", new EventParty("player", string.Empty, new GridCell(0, 0)), "left");

        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent("block-1", "side", "left") }));
    }

    [Test]
    public void DispatchUpdate_AfterDispatchContact_ResetsDirectionToNull()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "mover");
        ctx.Compile(subject, """
            $onContact = $other => { }
            $onUpdate = $delta => { }
            { "onContact": onContact, "onUpdate": onUpdate }
            """);

        ctx.Scheduler.DispatchContact("obj-1", new EventParty("player", string.Empty, new GridCell(0, 0)), "above");
        ctx.Scheduler.DispatchUpdate("obj-1", 0.016);

        Assert.That(ctx.Scheduler.CurrentEvent.Direction, Is.Null);
    }
}
