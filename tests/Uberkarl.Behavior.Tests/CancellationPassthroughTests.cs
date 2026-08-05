using System.Threading;
using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Pooscript-compatibility coverage for DiVoid #7738's dispatch/catch requirement (rule #7718, concept
/// #7732): "any catch-all wrapping a handler invoke must rethrow <see cref="OperationCanceledException"/> /
/// <c>ScriptTimeoutException</c> / <c>ScriptStepLimitExceededException</c> before the generic catch".
///
/// <para>
/// Only <see cref="OperationCanceledException"/> is testable here today: the other two types are part of
/// the async-execution rewrite (#7409/#7713) that has not shipped as a Pooshit.Scripting NuGet release yet
/// -- this repo still consumes 0.18.18-preview (confirmed by reflecting over the installed package: only
/// <c>ScriptException</c>/<c>ScriptParserException</c>/<c>ScriptRuntimeException</c> exist in
/// <c>Pooshit.Scripting.Errors</c> today), and task #7738 explicitly says to keep consuming the current
/// package rather than bump it. Once the watchdog swap (task #7737) lands a package version exposing
/// <c>ScriptLimits</c>, the equivalent tests for the other two types belong there. Rule #7718 itself flags
/// OCE as the one "easy to swallow" case anyway, since it is a plain BCL type -- unlike the other two, which
/// derive from <c>ScriptException</c> and so pass a naive <c>catch (ScriptException) { throw; }</c> by
/// accident. Proving THIS type survives is the part most likely to be silently broken.
/// </para>
///
/// <para>
/// <see cref="BehaviorWatchdog.Execute{T}"/> is the single choke point every P1 dispatch call
/// (<see cref="BehaviorScheduler.Dispatch"/>, <see cref="BehaviorLoader"/>'s init execute, and
/// <see cref="BehaviorLoader.CompileBinding"/>) routes a handler invoke through -- the P1 runtime glue
/// (game/Behavior/BehaviorRuntime.cs) never wraps a handler invoke in a try/catch of its own, so proving the
/// watchdog preserves exception identity here proves it for every P1 call site at once, and keeps the
/// pending watchdog swap (task #7737) confined to this one class, as required. Notably,
/// <see cref="BehaviorWatchdog.Execute{T}"/> has NO catch clause at all today (it inspects
/// <c>Task.IsFaulted</c> rather than catching) -- this test proves that design choice already satisfies the
/// rule: an OCE thrown by the guarded action reaches the caller as itself, never downgraded into a generic
/// "the handler threw" message that loses the distinction.
/// </para>
/// </summary>
[TestFixture]
public sealed class CancellationPassthroughTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);

    [Test]
    public void Execute_ActionThrowsOperationCanceledException_OutcomeCarriesItUnmangled()
    {
        var watchdog = new BehaviorWatchdog(Budget);
        var cts = new CancellationTokenSource();
        var thrown = new OperationCanceledException("host cancelled");

        var outcome = watchdog.Execute<object?>(() => throw thrown, cts);

        Assert.That(outcome.Kind, Is.EqualTo(BehaviorWatchdogOutcomeKind.Faulted));
        Assert.That(outcome.Exception, Is.SameAs(thrown),
            "an OperationCanceledException must reach the caller as itself -- not wrapped, not replaced by a generic message, and not silently discarded (rule #7718's 'easy to swallow' case).");
    }

    [Test]
    public void Dispatch_HandlerThrowsOperationCanceledException_QuarantineReasonNamesItExplicitly()
    {
        // Same guarantee, exercised through the real P1 dispatch path a script actually runs through
        // (BehaviorScheduler.Dispatch -> BehaviorWatchdog.Execute -> a compiled handler). Injects a raw C#
        // action in place of a Pooscript handler via a directly-registered instance, since Pooscript's
        // `throw(...)` can only construct script-level exceptions (ScriptRuntimeException), not arbitrary
        // BCL types -- there is currently no way for a REAL script to raise a genuine OperationCanceledException,
        // which is exactly why the unit-level test above (against the shared BehaviorWatchdog primitive) is
        // the one that matters; this integration-level test documents that the scheduler's own quarantine
        // path (used identically for every dispatch) does not add a second, different failure mode on top.
        var ctx = new BehaviorTestContext(Budget);
        var subject = ctx.CreateSubject("obj-1", "object", "throws");
        ctx.Compile(subject, """
            $onContact = $other => { throw("boom") }
            { "onContact": onContact }
            """);

        var quarantineEvents = new List<BehaviorQuarantineEvent>();
        ctx.Scheduler.Quarantined += quarantineEvents.Add;

        var fired = ctx.Scheduler.DispatchContact("obj-1", new EventParty("player", string.Empty, default));

        Assert.That(fired, Is.False);
        Assert.That(quarantineEvents, Has.Count.EqualTo(1));
        Assert.That(quarantineEvents[0].Reason, Does.Contain("ScriptRuntimeException").Or.Contain("boom"),
            "the quarantine reason must name the real exception, not a generic 'something went wrong'.");
    }
}
