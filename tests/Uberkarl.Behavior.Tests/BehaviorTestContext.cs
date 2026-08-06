namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Shared Godot-free test rig composing the reference facades (<see cref="BehaviorLevel"/>/
/// <see cref="BehaviorPlayer"/>/<see cref="BehaviorSubject"/>) with a <see cref="BehaviorLoader"/> and
/// <see cref="BehaviorScheduler"/> exactly the way a real (future, Godot) host would — this class itself IS
/// the "fake host" design #7704 §5.3 calls for, built from the core's own reference facade rather than a
/// second bespoke test double, per <see cref="BehaviorSubject"/>'s doc comment.
/// </summary>
internal sealed class BehaviorTestContext
{
    public BehaviorTestContext(TimeSpan? budget = null)
    {
        Intents = new IntentBuffer();
        Watchdog = new BehaviorWatchdog(budget ?? TimeSpan.FromMilliseconds(200));
        Loader = new BehaviorLoader(Watchdog);
        Scheduler = new BehaviorScheduler(Watchdog);
        Level = new BehaviorLevel(Intents);
        Player = new BehaviorPlayer(Intents);
    }

    public IntentBuffer Intents { get; }

    public BehaviorWatchdog Watchdog { get; }

    public BehaviorLoader Loader { get; }

    public BehaviorScheduler Scheduler { get; }

    public BehaviorLevel Level { get; }

    public BehaviorPlayer Player { get; }

    public BehaviorSubject CreateSubject(string id, string kind, string name = "") => new(id, kind, name, Intents);

    /// <summary>Compiles <paramref name="source"/> with <paramref name="subject"/> bound as <c>self</c> alongside the shared <c>level</c>/<c>player</c>/<c>event</c> globals, and registers the result with <see cref="Scheduler"/>.</summary>
    public BehaviorInstance Compile(BehaviorSubject subject, string source)
    {
        var compiled = Loader.Compile(source, Globals(subject));
        var instance = new BehaviorInstance(subject.Id, compiled);
        Scheduler.Register(instance);
        return instance;
    }

    /// <summary>
    /// Compiles a <see cref="ResolvedBehaviorBinding"/> (script or predefined) via <see cref="BehaviorLoader.CompileBinding"/>
    /// -- the P1 runtime wiring's entry point (DiVoid #7738) -- with <paramref name="subject"/> bound as
    /// <c>self</c>, and registers the result with <see cref="Scheduler"/>.
    /// </summary>
    public CompiledBehavior CompileResolved(BehaviorSubject subject, ResolvedBehaviorBinding binding)
    {
        var compiled = Loader.CompileBinding(binding, Globals(subject));
        Scheduler.Register(new BehaviorInstance(subject.Id, compiled));
        return compiled;
    }

    private Dictionary<string, object> Globals(BehaviorSubject subject) => new()
    {
        ["self"] = subject,
        ["level"] = Level,
        ["player"] = Player,
        ["event"] = Scheduler.CurrentEvent,
    };
}
