namespace Uberkarl.Behavior;

using System.Collections;
using System.Threading;
using Pooshit.Scripting.Errors;
using Pooshit.Scripting.Parser;
using Pooshit.Scripting.Providers;

/// <summary>
/// Turns Pooscript text into a reusable <see cref="CompiledBehavior"/> (design #7704 §5.3): parses once,
/// runs a one-time "init execute" with the facade globals bound, and reads back the handler lambdas the
/// script assigned. Also the seat of the capability boundary (design #7704 §8.2/C-8) — the parser this
/// builds registers ONLY the caller-supplied facade globals; <c>import</c>/<c>new</c>/casts are off, so a
/// behavior script can never reach reflection, file, network, or any type beyond what the globals expose.
///
/// <para>
/// Handler convention (design §7, D-1): the script assigns named handler lambdas to top-level variables
/// (e.g. <c>$onContact = $other =&gt; { ... }</c>) and, as its LAST statement, returns a dictionary literal
/// echoing them back — <c>{ "onContact": onContact, "onUpdate": onUpdate }</c>. This is required because
/// Pooscript's top-level variable assignments do not persist into the host-supplied variable provider after
/// <c>Execute</c> returns (verified empirically against Pooshit.Scripting 0.18.18-preview): only the
/// explicit return value survives. Handler bodies should end with their result as the last expression rather
/// than an explicit <c>return</c> — <c>return</c> inside a lambda invoked later via the cached delegate does
/// not propagate a value in this Pooscript version (also verified empirically); this does not affect the
/// behavior layer in practice because handlers communicate only through recorded intents, never return
/// values.
/// </para>
/// </summary>
public sealed class BehaviorLoader
{
    private readonly BehaviorWatchdog watchdog;

    public BehaviorLoader(BehaviorWatchdog watchdog) => this.watchdog = watchdog;

    /// <summary>
    /// Compiles <paramref name="source"/> against the given facade globals (typically <c>self</c>,
    /// <c>level</c>, <c>player</c>, <c>event</c> — design #7704 §8.1/§8.2). Always returns a usable
    /// <see cref="CompiledBehavior"/>: a parse error, an init-time exception, or an init-time budget breach
    /// all produce an already-quarantined result rather than throwing, so callers never need a separate
    /// failure path — they register the result with a <see cref="BehaviorScheduler"/> exactly like a
    /// healthy one, and dispatch simply becomes a no-op for it.
    /// </summary>
    public CompiledBehavior Compile(string source, IReadOnlyDictionary<string, object> facadeGlobals)
    {
        var instanceCancellation = new CancellationTokenSource();

        Pooshit.Scripting.IScript script;
        try
        {
            script = CreateSandboxedParser().Parse(source);
        }
        catch (ScriptParserException ex)
        {
            var quarantined = new CompiledBehavior(EmptyHandlers, instanceCancellation);
            quarantined.Quarantine($"parse error: {ex.Message}");
            return quarantined;
        }

        var initVariables = new Dictionary<string, object>(facadeGlobals);
        var outcome = watchdog.Execute(() => script.Execute(initVariables), instanceCancellation);

        if (outcome.Kind != BehaviorWatchdogOutcomeKind.Completed)
        {
            var quarantined = new CompiledBehavior(EmptyHandlers, instanceCancellation);
            quarantined.Quarantine(outcome.Kind == BehaviorWatchdogOutcomeKind.BudgetExceeded
                ? $"init exceeded the {watchdog.Budget.TotalMilliseconds}ms watchdog budget"
                : $"init threw: {outcome.Exception}");
            return quarantined;
        }

        return new CompiledBehavior(ExtractHandlers(outcome.Value), instanceCancellation);
    }

    private static ScriptParser CreateSandboxedParser() => new()
    {
        // Capability lock (design #7704 §8.2, C-8/#2946): only the facade globals the caller binds are
        // reachable. Control flow (if/while/for/...) stays ON -- behavior scripts need it, and the watchdog
        // (not a restricted grammar) is what makes an unbounded loop safe.
        TypeInstanceProvidersEnabled = false,
        TypeCastsEnabled = false,
        ImportsEnabled = false,
    };

    private static IReadOnlyDictionary<BehaviorEventKind, BehaviorHandler> ExtractHandlers(object? initResult)
    {
        if (initResult is not IDictionary raw)
            return EmptyHandlers;

        var handlers = new Dictionary<BehaviorEventKind, BehaviorHandler>();
        foreach (DictionaryEntry entry in raw)
        {
            if (entry.Key is string name
                && BehaviorEventNames.TryParse(name, out var kind)
                && entry.Value is LambdaMethod lambda)
            {
                handlers[kind] = new BehaviorHandler(arguments => lambda.Invoke(arguments!));
            }
        }

        return handlers;
    }

    private static readonly IReadOnlyDictionary<BehaviorEventKind, BehaviorHandler> EmptyHandlers =
        new Dictionary<BehaviorEventKind, BehaviorHandler>();
}
