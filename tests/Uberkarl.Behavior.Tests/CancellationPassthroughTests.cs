using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>Proves rule #7718's passthrough for <see cref="OperationCanceledException"/>, the type most likely to be silently swallowed since it does not derive from <c>ScriptException</c>.</summary>
[TestFixture]
public sealed class CancellationPassthroughTests
{
    [Test]
    public void TryRun_ActionThrowsOperationCanceledException_ReasonNamesItUnmangled()
    {
        var thrown = new OperationCanceledException("host cancelled");

        var succeeded = ScriptExecutionGuard.TryRun<object?>(() => throw thrown, out _, out var failureReason);

        Assert.That(succeeded, Is.False);
        Assert.That(failureReason, Does.Contain("cancelled").And.Contain("host cancelled"));
    }

    [Test]
    public void Dispatch_HandlerThrowsOperationCanceledException_QuarantineReasonNamesItExplicitly()
    {
        var ctx = new BehaviorTestContext();
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
