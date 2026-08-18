using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Covers DiVoid #8237 item 1: a script that harvests to zero handlers used to compile clean, load, run and do
/// nothing -- indistinguishable from a behavior that legitimately had nothing to say. Every shape below is now
/// quarantined with a reason that names what was rejected, so an author can tell "my script is wrong" from
/// "the engine ignored it".
/// </summary>
[TestFixture]
public sealed class MisshapedScriptQuarantineTests
{
    [Test]
    public void TypoedHandlerName_IsQuarantined_AndTheReasonNamesTheTypo()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");

        var instance = ctx.Compile(subject, """
            $onUpdte = $delta => { self.moveBy(1, 0); }
            { "onUpdte": onUpdte }
            """);

        Assert.That(instance.IsQuarantined, Is.True);
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("onUpdte"),
            "the author's misspelling is the one piece of information that makes this diagnosable");
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("not an event name"));
    }

    [Test]
    public void ScriptNotEndingWithAMap_IsQuarantined()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");

        var instance = ctx.Compile(subject, """
            $onContact = $other => { player.hurt(1); }
            42
            """);

        Assert.That(instance.IsQuarantined, Is.True);
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("map of handler lambdas"));
    }

    [Test]
    public void HandlerEntryThatIsNotAFunction_IsQuarantined_AndTheReasonNamesTheEntry()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");

        var instance = ctx.Compile(subject, """
            { "onContact": 7 }
            """);

        Assert.That(instance.IsQuarantined, Is.True);
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("onContact"));
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("not a function"));
    }

    [Test]
    public void AScriptThatDeclaresSomeUsableHandlers_StillCompiles_EvenIfOtherEntriesAreRejected()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");

        var instance = ctx.Compile(subject, """
            $onContact = $other => { player.hurt(3); }
            $onUpdte = $delta => { self.moveBy(1, 0); }
            { "onContact": onContact, "onUpdte": onUpdte }
            """);

        Assert.That(instance.IsQuarantined, Is.False,
            "quarantining a working behavior over one typo'd sibling would be a worse trade than the silence it replaces");

        var fired = ctx.Scheduler.DispatchContact("spike-1", new EventParty("player", string.Empty, new GridCell(1, 1)));

        Assert.That(fired, Is.True);
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new HurtIntent(BehaviorSubjectIds.Player, 3) }));
    }

    [Test]
    public void AWellFormedScript_IsNotQuarantined()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("spike-1", "tile", "spike");

        var instance = ctx.Compile(subject, """
            $onContact = $other => { player.hurt(1); }
            { "onContact": onContact }
            """);

        Assert.That(instance.IsQuarantined, Is.False);
    }

    [Test]
    public void AnExplicitlyEmptyMap_IsAccepted_AsTheDeliberateNoHandlersOptOut()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("level", "level");

        var instance = ctx.Compile(subject, """
            self.setState("seeded", true);
            {}
            """);

        Assert.That(instance.IsQuarantined, Is.False,
            "a script that says in so many words that it has no handlers is not the mistake this guard is for");
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent("level", "seeded", true) }),
            "its top-level init work must still have landed");
    }
}
