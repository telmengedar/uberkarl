namespace Uberkarl.Behavior;

/// <summary>Editor-side parse/compile feedback: runs <see cref="BehaviorLoader.Compile"/> — the runtime's own call — against detached, inert facades, and reports the quarantine reason, or none.</summary>
public static class BehaviorSourceValidator
{
    /// <summary>Compiles <paramref name="source"/> under <paramref name="role"/>'s budget against fresh, disposable facades. Returns the quarantine reason, or <c>null</c> when it compiles clean. Never throws for author input — every failure is a reason.</summary>
    public static string? Validate(string source, BehaviorScriptRole role)
    {
        var intents = new IntentBuffer();
        var self = new BehaviorSubject("editor-preview", string.Empty, string.Empty, intents);
        var level = new BehaviorLevel(intents);
        var player = new BehaviorPlayer(intents);
        var scheduler = new BehaviorScheduler();

        var loader = new BehaviorLoader(BehaviorScriptBudgets.DefaultBehavior(), BehaviorScriptBudgets.DefaultInit());
        var compiled = loader.Compile(source, BehaviorGlobals.Compose(self, level, player, scheduler.CurrentEvent), role);
        return compiled.QuarantineReason;
    }
}
