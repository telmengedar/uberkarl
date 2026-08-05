using System.Diagnostics;
using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Covers the P0 acceptance gate's load-bearing requirement (DiVoid #7710): "a looping / CPU-burning /
/// throwing script is quarantined (budget breach kills the handler, subject degrades) and the test host
/// keeps running" — demonstrated in a test that would hang if the watchdog only relied on cooperative
/// Pooscript cancellation (design #7704 §8.4: #7409 is open, and a cached handler's cancellation token is
/// fixed at parse+init time — see <see cref="BehaviorWatchdog"/>'s doc comment for what was verified).
/// Every test here asserts elapsed wall-clock time to prove the CALLING thread — this test process, i.e.
/// "the host" — was never blocked past the watchdog budget, regardless of whether the runaway script
/// thread itself ever actually stops.
/// </summary>
[TestFixture]
public sealed class WatchdogQuarantineTests
{
    // Small and deterministic so these tests run fast while still proving the guarantee: even a script that
    // spins forever and never checks cancellation cannot block a caller past roughly this budget.
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(150);

    // Generous upper bound for "the host kept running" -- if the watchdog were broken (e.g. it awaited the
    // runaway task instead of abandoning it) this assertion would fail (or the test would hang until the
    // fixture's own kill switch, never silently pass).
    private static readonly TimeSpan MustReturnWithin = TimeSpan.FromSeconds(3);

    [Test]
    [CancelAfter(10_000)]
    public void LoopingHandler_IsQuarantined_AndTheHostKeepsRunning()
    {
        var ctx = new BehaviorTestContext(Budget);
        var subject = ctx.CreateSubject("obj-1", "object", "runaway");
        var quarantineEvents = new List<BehaviorQuarantineEvent>();
        ctx.Scheduler.Quarantined += quarantineEvents.Add;

        // A tight `while(true)` with a non-trivial body -- exactly the shape design #7704 §8.4 flags as not
        // yet guaranteed-interruptible by Pooscript's own (incomplete, #7409) cooperative cancellation.
        var instance = ctx.Compile(subject, """
            $onUpdate = $delta => { while(true) { $x = 1; } }
            { "onUpdate": onUpdate }
            """);
        Assert.That(instance.IsQuarantined, Is.False, "init itself must not loop -- only the onUpdate body does");

        var stopwatch = Stopwatch.StartNew();
        var fired = ctx.Scheduler.DispatchUpdate("obj-1", 0.016);
        stopwatch.Stop();

        Assert.That(fired, Is.False);
        Assert.That(stopwatch.Elapsed, Is.LessThan(MustReturnWithin),
            "the scheduler must return control near the watchdog budget -- this is the freeze-proof guarantee itself");
        Assert.That(ctx.Scheduler.IsQuarantined("obj-1"), Is.True);
        Assert.That(quarantineEvents, Has.Count.EqualTo(1));
        Assert.That(quarantineEvents[0].SubjectId, Is.EqualTo("obj-1"));
        Assert.That(quarantineEvents[0].TriggeringEvent, Is.EqualTo(BehaviorEventKind.OnUpdate));
        Assert.That(quarantineEvents[0].Reason, Does.Contain("budget"));

        // The host is demonstrably still alive and responsive: an unrelated, healthy subject dispatches fine
        // right after the runaway one -- nothing about the leaked background thread affects it.
        var healthy = ctx.CreateSubject("obj-2", "object", "healthy");
        ctx.Compile(healthy, """
            $onUpdate = $delta => { self.setState("ok", true); }
            { "onUpdate": onUpdate }
            """);
        Assert.That(ctx.Scheduler.DispatchUpdate("obj-2", 0.016), Is.True);

        // Quarantine is permanent and silent from here on: re-dispatching the runaway subject is a pure
        // no-op and does NOT raise a second Quarantined notification (design #7704 §8.3 -- "logged once").
        Assert.That(ctx.Scheduler.DispatchUpdate("obj-1", 0.016), Is.False);
        Assert.That(quarantineEvents, Has.Count.EqualTo(1));
    }

    [Test]
    [CancelAfter(10_000)]
    public void ThrowingHandler_IsQuarantined_AndTheHostKeepsRunning()
    {
        var ctx = new BehaviorTestContext(Budget);
        var subject = ctx.CreateSubject("obj-1", "object", "buggy");
        var instance = ctx.Compile(subject, """
            $onContact = $other => { throw("boom") }
            { "onContact": onContact }
            """);
        Assert.That(instance.IsQuarantined, Is.False);

        var stopwatch = Stopwatch.StartNew();
        var fired = ctx.Scheduler.DispatchContact("obj-1", new EventParty("player", string.Empty, default));
        stopwatch.Stop();

        Assert.That(fired, Is.False);
        Assert.That(stopwatch.Elapsed, Is.LessThan(MustReturnWithin));
        Assert.That(ctx.Scheduler.IsQuarantined("obj-1"), Is.True);
        Assert.That(ctx.Intents.Intents, Is.Empty, "a thrown handler must not leave partial intents behind");
    }

    [Test]
    [CancelAfter(10_000)]
    public void LoopingInit_IsQuarantined_BeforeRegistration_CompileNeverHangs()
    {
        var ctx = new BehaviorTestContext(Budget);
        var subject = ctx.CreateSubject("obj-1", "object", "runaway-init");

        var stopwatch = Stopwatch.StartNew();
        var instance = ctx.Compile(subject, "while(true) { $x = 1; }");
        stopwatch.Stop();

        Assert.That(stopwatch.Elapsed, Is.LessThan(MustReturnWithin),
            "a malicious init body must not block Compile -- the watchdog guards init exactly like a handler dispatch");
        Assert.That(instance.IsQuarantined, Is.True);
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("budget"));
    }

    [Test]
    public void ParseError_IsQuarantined_NotThrown()
    {
        var ctx = new BehaviorTestContext(Budget);
        var subject = ctx.CreateSubject("obj-1", "object", "malformed");

        var instance = ctx.Compile(subject, "$onContact = $other => { ");

        Assert.That(instance.IsQuarantined, Is.True);
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("parse error"));
    }
}
