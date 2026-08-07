using Pooshit.Scripting;

namespace Uberkarl.Behavior.Tests;

/// <summary>Shared Godot-free test rig composing the reference facades with a <see cref="BehaviorLoader"/> and <see cref="BehaviorScheduler"/> exactly the way a real host would.</summary>
internal sealed class BehaviorTestContext
{
    public BehaviorTestContext(ScriptLimits? behaviorLimits = null, ScriptLimits? initLimits = null)
    {
        Intents = new IntentBuffer();
        Loader = new BehaviorLoader(behaviorLimits ?? BehaviorScriptBudgets.DefaultBehavior(), initLimits ?? BehaviorScriptBudgets.DefaultInit());
        Scheduler = new BehaviorScheduler();
        Level = new BehaviorLevel(Intents);
        Player = new BehaviorPlayer(Intents);
    }

    public IntentBuffer Intents { get; }

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
