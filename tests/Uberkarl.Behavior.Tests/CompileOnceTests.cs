using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Covers the P0 acceptance gate's third requirement (DiVoid #7710): "parse+init happens once (assert
/// handlers aren't re-parsed per event)" — design #7704 §7's chosen handler-registration model vs. the
/// explicitly rejected "re-execute whole script per event" alternative.
/// </summary>
[TestFixture]
public sealed class CompileOnceTests
{
    [Test]
    public void InitOnlyStatement_RunsExactlyOnce_RegardlessOfHowManyEventsDispatchAfterward()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "gate");

        // self.setGraphic runs at the TOP LEVEL of the script -- outside any handler -- so it only ever
        // executes during the one-time init. If the loader instead re-executed the whole script per
        // dispatch (the rejected alternative), this call would re-fire on every onUpdate too.
        const string source = """
            self.setGraphic("initialized")
            $onUpdate = $delta => { self.setState("ticks", delta); }
            { "onUpdate": onUpdate }
            """;

        ctx.Compile(subject, source);

        var initIntents = ctx.Intents.Drain();
        Assert.That(initIntents, Is.EqualTo(new BehaviorIntent[] { new SetGraphicIntent("obj-1", "initialized") }),
            "the init-only statement must have run exactly once during Compile");

        const int dispatchCount = 5;
        for (var i = 0; i < dispatchCount; i++)
            Assert.That(ctx.Scheduler.DispatchUpdate("obj-1", 0.1), Is.True);

        var afterDispatch = ctx.Intents.Intents;
        Assert.That(afterDispatch.OfType<SetGraphicIntent>(), Is.Empty,
            "a re-parse-per-event implementation would re-run the init-only setGraphic call on every dispatch");
        Assert.That(afterDispatch.OfType<SetStateIntent>().Count(), Is.EqualTo(dispatchCount));
    }

    [Test]
    public void CompiledHandlerDelegate_IsTheSameInstance_AcrossDispatches()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "gate");
        var instance = ctx.Compile(subject, """
            $onUpdate = $delta => { delta }
            { "onUpdate": onUpdate }
            """);

        var handlerBeforeDispatch = instance.Compiled.Handlers[BehaviorEventKind.OnUpdate];

        ctx.Scheduler.DispatchUpdate("obj-1", 0.1);
        ctx.Scheduler.DispatchUpdate("obj-1", 0.2);
        ctx.Scheduler.DispatchUpdate("obj-1", 0.3);

        Assert.That(instance.Compiled.Handlers[BehaviorEventKind.OnUpdate], Is.SameAs(handlerBeforeDispatch),
            "the cached handler must be reused, never recompiled, across dispatches");
    }
}
