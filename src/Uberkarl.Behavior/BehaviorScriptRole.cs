namespace Uberkarl.Behavior;

/// <summary>Which <see cref="BehaviorScriptBudgets"/> entry a compiled script runs under.</summary>
public enum BehaviorScriptRole
{
    /// <summary>A tile, trigger, or object behavior script.</summary>
    Behavior,

    /// <summary>The level script.</summary>
    Init,
}
