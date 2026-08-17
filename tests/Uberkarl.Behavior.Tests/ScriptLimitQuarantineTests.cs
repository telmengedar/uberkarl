using System.Diagnostics;
using NUnit.Framework;
using Pooshit.Scripting;

namespace Uberkarl.Behavior.Tests;

/// <summary>Proves the native <c>ScriptLimits</c> guards quarantine an over-budget subject while the host keeps running (DiVoid #7737).</summary>
[TestFixture]
public sealed class ScriptLimitQuarantineTests
{
    private static readonly ScriptLimits TinyStepBudget = new() { MaxSteps = 500, Timeout = TimeSpan.FromSeconds(5) };
    private static readonly ScriptLimits TinyDepthBudget = new() { MaxDepth = 5, Timeout = TimeSpan.FromSeconds(5) };
    private static readonly ScriptLimits TinyMemoryBudget = new() { MaxVariableBytes = 4096, MaxSteps = 1_000_000, Timeout = TimeSpan.FromSeconds(5) };
    private static readonly TimeSpan MustReturnWithin = TimeSpan.FromSeconds(3);

    [Test]
    [CancelAfter(10_000)]
    public void LoopingHandler_IsQuarantined_AndTheHostKeepsRunning()
    {
        var ctx = new BehaviorTestContext(TinyStepBudget);
        var subject = ctx.CreateSubject("obj-1", "object", "runaway");
        var quarantineEvents = new List<BehaviorQuarantineEvent>();
        ctx.Scheduler.Quarantined += quarantineEvents.Add;

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
            "the scheduler must return control near the configured budget -- this is the freeze-proof guarantee itself");
        Assert.That(ctx.Scheduler.IsQuarantined("obj-1"), Is.True);
        Assert.That(quarantineEvents, Has.Count.EqualTo(1));
        Assert.That(quarantineEvents[0].SubjectId, Is.EqualTo("obj-1"));
        Assert.That(quarantineEvents[0].TriggeringEvent, Is.EqualTo(BehaviorEventKind.OnUpdate));
        Assert.That(quarantineEvents[0].Reason, Does.Contain("budget").And.Contain("ScriptStepLimitExceededException"));

        var healthy = ctx.CreateSubject("obj-2", "object", "healthy");
        ctx.Compile(healthy, """
            $onUpdate = $delta => { self.setState("ok", true); }
            { "onUpdate": onUpdate }
            """);
        Assert.That(ctx.Scheduler.DispatchUpdate("obj-2", 0.016), Is.True);

        Assert.That(ctx.Scheduler.DispatchUpdate("obj-1", 0.016), Is.False);
        Assert.That(quarantineEvents, Has.Count.EqualTo(1), "quarantine is permanent and logged exactly once");
    }

    [Test]
    [CancelAfter(10_000)]
    public void ThrowingHandler_IsQuarantined_AndTheHostKeepsRunning()
    {
        var ctx = new BehaviorTestContext();
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
        var ctx = new BehaviorTestContext(TinyStepBudget);
        var subject = ctx.CreateSubject("obj-1", "object", "runaway-init");

        var stopwatch = Stopwatch.StartNew();
        var instance = ctx.Compile(subject, "while(true) { $x = 1; }");
        stopwatch.Stop();

        Assert.That(stopwatch.Elapsed, Is.LessThan(MustReturnWithin),
            "a malicious init body must not block Compile -- the guard applies to init exactly like a handler dispatch");
        Assert.That(instance.IsQuarantined, Is.True);
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("budget"));
    }

    [Test]
    [CancelAfter(10_000)]
    public void DeepRecursion_IsQuarantined_ViaMaxDepth()
    {
        var ctx = new BehaviorTestContext(TinyDepthBudget);
        var subject = ctx.CreateSubject("obj-1", "object", "recursive");
        ctx.Compile(subject, """
            $recurse = $n => { recurse(n + 1) }
            $onUpdate = $delta => { recurse(0) }
            { "onUpdate": onUpdate }
            """);

        var fired = ctx.Scheduler.DispatchUpdate("obj-1", 0.016);

        Assert.That(fired, Is.False);
        Assert.That(ctx.Scheduler.IsQuarantined("obj-1"), Is.True);
    }

    [Test]
    [CancelAfter(10_000)]
    public void OversizedVariable_IsQuarantined_ViaMaxVariableBytes()
    {
        var ctx = new BehaviorTestContext(TinyMemoryBudget);
        var subject = ctx.CreateSubject("obj-1", "object", "memoryhog");
        ctx.Compile(subject, """
            $onUpdate = $delta => { $s = "x"; while(true) { s = s + s; } }
            { "onUpdate": onUpdate }
            """);

        var fired = ctx.Scheduler.DispatchUpdate("obj-1", 0.016);

        Assert.That(fired, Is.False);
        Assert.That(ctx.Scheduler.IsQuarantined("obj-1"), Is.True);
    }

    [Test]
    public void ParseError_IsQuarantined_NotThrown()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "malformed");

        var instance = ctx.Compile(subject, "$onContact = $other => { ");

        Assert.That(instance.IsQuarantined, Is.True);
        Assert.That(instance.Compiled.QuarantineReason, Does.Contain("parse error"));
    }

    [Test]
    [CancelAfter(30_000)]
    public void PatrolHandler_DispatchedTenThousandTimes_UnderDefaultBudget_NeverQuarantines()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("object:0", "object", "platform");
        subject.Position = new BehaviorVector2(0, 0);
        var binding = ResolvedBehaviorBinding.FromPredefined(PredefinedBehaviors.Patrol);
        var compiled = ctx.CompileResolved(subject, binding);
        Assert.That(compiled.IsQuarantined, Is.False);
        subject.SeedState("dir", 1);
        subject.SeedState("origin", 0.0);

        for (var frame = 0; frame < 10_000; frame++)
            Assert.That(ctx.Scheduler.DispatchUpdate("object:0", 1.0 / 60), Is.True, $"quarantined at dispatch #{frame}");

        Assert.That(ctx.Scheduler.IsQuarantined("object:0"), Is.False);
    }

    [Test]
    [CancelAfter(10_000)]
    public void RunawaySingleDispatch_StillQuarantines_UnderTheRealProductionBudget()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "runaway");
        ctx.Compile(subject, """
            $onUpdate = $delta => { while(true) { $x = 1; } }
            { "onUpdate": onUpdate }
            """);
        var quarantineEvents = new List<BehaviorQuarantineEvent>();
        ctx.Scheduler.Quarantined += quarantineEvents.Add;

        Assert.That(ctx.Scheduler.DispatchUpdate("obj-1", 0.016), Is.False);
        Assert.That(ctx.Scheduler.IsQuarantined("obj-1"), Is.True);
        Assert.That(quarantineEvents, Has.Count.EqualTo(1));
        Assert.That(quarantineEvents[0].Reason, Does.Contain("ScriptStepLimitExceededException"),
            "a step-burning loop must still be caught by MaxSteps, not by the wall-clock Timeout -- keeps the two guards distinguishable");
    }

    [Test]
    [CancelAfter(10_000)]
    public void BlockingHostCall_WithoutConsumingSteps_IsCaughtByTheWallClockTimeout_NotByStepBudget()
    {
        const string Source = """
            $onUpdate = $delta => {
                stall.block(300);
                self.setState("done", true);
            }
            { "onUpdate": onUpdate }
            """;

        var infiniteTimeout = new ScriptLimits { MaxSteps = 4_000, MaxDepth = 8, MaxVariables = 24, MaxVariableBytes = 32 * 1024 };
        var ctxNoTimeout = new BehaviorTestContext(infiniteTimeout);
        var subjectNoTimeout = ctxNoTimeout.CreateSubject("obj-1", "object", "stalls");
        var compiledNoTimeout = ctxNoTimeout.Loader.Compile(Source, StallGlobals(subjectNoTimeout));
        Assert.That(compiledNoTimeout.IsQuarantined, Is.False);
        ctxNoTimeout.Scheduler.Register(new BehaviorInstance("obj-1", compiledNoTimeout));
        Assert.That(ctxNoTimeout.Scheduler.DispatchUpdate("obj-1", 0.016), Is.True,
            "without a configured Timeout, a step-cheap blocking host call has nothing left to catch it -- proves the backstop is not vacuous");

        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "stalls");
        var compiled = ctx.Loader.Compile(Source, StallGlobals(subject));
        Assert.That(compiled.IsQuarantined, Is.False);
        ctx.Scheduler.Register(new BehaviorInstance("obj-1", compiled));
        var quarantineEvents = new List<BehaviorQuarantineEvent>();
        ctx.Scheduler.Quarantined += quarantineEvents.Add;

        Assert.That(ctx.Scheduler.DispatchUpdate("obj-1", 0.016), Is.False);
        Assert.That(ctx.Scheduler.IsQuarantined("obj-1"), Is.True);
        Assert.That(quarantineEvents, Has.Count.EqualTo(1));
        Assert.That(quarantineEvents[0].Reason, Does.Contain("ScriptTimeoutException"),
            "a 300ms blocking host call spends only a handful of steps -- MaxSteps=4000 cannot see it, only the wall-clock Timeout can");
    }

    private static Dictionary<string, object> StallGlobals(BehaviorSubject subject) => new()
    {
        ["self"] = subject,
        ["stall"] = new BlockingHostCallStub(),
    };

    [Test]
    [CancelAfter(10_000)]
    public void TwoCompiledBehaviors_KeepIndependentPerDispatchBudgets_WhenDispatchedInterleaved()
    {
        var ctx = new BehaviorTestContext(TinyStepBudget);
        var subjectA = ctx.CreateSubject("obj-a", "object", "healthy-a");
        var subjectB = ctx.CreateSubject("obj-b", "object", "healthy-b");
        const string HealthySource = """
            $onUpdate = $delta => { self.setState("ticks", delta); }
            { "onUpdate": onUpdate }
            """;
        ctx.Compile(subjectA, HealthySource);
        ctx.Compile(subjectB, HealthySource);

        for (var i = 0; i < 100; i++)
        {
            Assert.That(ctx.Scheduler.DispatchUpdate("obj-a", 0.016), Is.True, $"obj-a dispatch #{i}");
            Assert.That(ctx.Scheduler.DispatchUpdate("obj-b", 0.016), Is.True, $"obj-b dispatch #{i}");
        }

        Assert.That(ctx.Scheduler.IsQuarantined("obj-a"), Is.False);
        Assert.That(ctx.Scheduler.IsQuarantined("obj-b"), Is.False);
    }
}
