namespace Uberkarl.Behavior;

/// <summary>
/// A cached, invokable event handler discovered during the one-time script init execute (design #7704 §7,
/// D-1). Wraps a compiled Pooscript lambda (<c>Pooshit.Scripting.Providers.LambdaMethod</c>) behind a plain
/// delegate so no Pooscript type ever appears in this core's public surface outside <see cref="BehaviorLoader"/>.
/// </summary>
public sealed class BehaviorHandler
{
    private readonly Func<object?[], object?> invoke;

    internal BehaviorHandler(Func<object?[], object?> invoke) => this.invoke = invoke;

    /// <summary>Invokes the cached handler with positional arguments matching its event's signature (design #7704 §7 event table).</summary>
    public object? Invoke(params object?[] arguments) => invoke(arguments);
}
