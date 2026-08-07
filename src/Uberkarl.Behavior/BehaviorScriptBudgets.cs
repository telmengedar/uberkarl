namespace Uberkarl.Behavior;

using Pooshit.Scripting;

/// <summary>Default per-<see cref="BehaviorScriptRole"/> <see cref="ScriptLimits"/> (DiVoid #7737).</summary>
public static class BehaviorScriptBudgets
{
    /// <summary>Tight defaults for a tile/trigger/object behavior script.</summary>
    public static ScriptLimits DefaultBehavior() => new() {
        Timeout = TimeSpan.FromMilliseconds(20),
        MaxSteps = 4_000,
        MaxDepth = 8,
        MaxVariables = 24,
        MaxVariableBytes = 32 * 1024,
        RegexTimeout = TimeSpan.FromMilliseconds(25),
    };

    /// <summary>Raised defaults for the level script's one-time init/setup work.</summary>
    public static ScriptLimits DefaultInit() => new() {
        Timeout = TimeSpan.FromMilliseconds(150),
        MaxSteps = 40_000,
        MaxDepth = 12,
        MaxVariables = 128,
        MaxVariableBytes = 512 * 1024,
        RegexTimeout = TimeSpan.FromMilliseconds(50),
    };
}
