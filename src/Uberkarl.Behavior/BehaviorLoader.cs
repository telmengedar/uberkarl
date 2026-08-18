namespace Uberkarl.Behavior;

using System.Collections;
using Pooshit.Scripting;
using Pooshit.Scripting.Errors;
using Pooshit.Scripting.Parser;
using Pooshit.Scripting.Providers;

/// <summary>Compiles Pooscript text into a reusable <see cref="CompiledBehavior"/>: parses once, runs a one-time init execute with the facade globals bound, and reads back the handler lambdas the script assigned.</summary>
public sealed class BehaviorLoader
{
    private readonly ScriptLimits behaviorLimits;
    private readonly ScriptLimits initLimits;

    /// <summary>Creates a new <see cref="BehaviorLoader"/>.</summary>
    /// <param name="behaviorLimits">Budget applied to tile/trigger/object behavior scripts.</param>
    /// <param name="initLimits">Budget applied to the level script.</param>
    public BehaviorLoader(ScriptLimits behaviorLimits, ScriptLimits initLimits)
    {
        this.behaviorLimits = behaviorLimits ?? throw new ArgumentNullException(nameof(behaviorLimits));
        this.initLimits = initLimits ?? throw new ArgumentNullException(nameof(initLimits));
    }

    /// <summary>Compiles a <see cref="ResolvedBehaviorBinding"/> (script or predefined) against the given facade globals.</summary>
    public CompiledBehavior CompileBinding(ResolvedBehaviorBinding binding, IReadOnlyDictionary<string, object> facadeGlobals, BehaviorScriptRole role = BehaviorScriptRole.Behavior)
    {
        if (binding is null)
            throw new ArgumentNullException(nameof(binding));

        if (binding.IsScript)
            return Compile(binding.Script!, facadeGlobals, role);

        string source;
        try {
            if (!PredefinedBehaviors.TryGetSource(binding.PredefinedId!, binding.Parameters, out source))
                return Quarantined($"unknown predefined behavior id '{binding.PredefinedId}'");
        }
        catch (FormatException ex) {
            // A package can legally declare a non-numeric parameter, so this is bad content, not a bug --
            // and this method promises to always return a usable result (#8237 item 5).
            return Quarantined($"predefined behavior '{binding.PredefinedId}': {ex.Message}");
        }

        return Compile(source, facadeGlobals, role);
    }

    /// <summary>Compiles <paramref name="source"/> against the given facade globals.</summary>
    public CompiledBehavior Compile(string source, IReadOnlyDictionary<string, object> facadeGlobals, BehaviorScriptRole role = BehaviorScriptRole.Behavior)
    {
        Pooshit.Scripting.IScript script;
        try {
            script = CreateSandboxedParser(role).Parse(source);
        }
        catch (ScriptParserException ex) {
            return Quarantined($"parse error: {ex.Message}");
        }

        var initVariables = new Dictionary<string, object>(facadeGlobals);
        if (!ScriptExecutionGuard.TryRun(() => script.Execute(initVariables), out var initResult, out var failureReason))
            return Quarantined($"init {failureReason}");

        return FromInitResult(initResult);
    }

    private ScriptParser CreateSandboxedParser(BehaviorScriptRole role) => new() {
        TypeInstanceProvidersEnabled = false,
        TypeCastsEnabled = false,
        ImportsEnabled = false,
        Limits = role == BehaviorScriptRole.Init ? initLimits : behaviorLimits,
    };

    private static CompiledBehavior Quarantined(string reason)
    {
        var behavior = new CompiledBehavior(EmptyHandlers);
        behavior.Quarantine(reason);
        return behavior;
    }

    /// <summary>
    /// Turns what the init execute evaluated to into a compiled behavior. A script that produced nothing usable
    /// is quarantined with a reason naming what was rejected, rather than compiling into a behavior with zero
    /// handlers that loads clean and silently never reacts (DiVoid #8237 item 1).
    /// </summary>
    private static CompiledBehavior FromInitResult(object? initResult)
    {
        if (initResult is not IDictionary raw)
            return Quarantined($"script must end with a map of handler lambdas, but ended with {Describe(initResult)}");

        var handlers = new Dictionary<BehaviorEventKind, BehaviorHandler>();
        var rejected = new List<string>();

        foreach (DictionaryEntry entry in raw)
        {
            if (entry.Key is not string name) {
                rejected.Add($"a {Describe(entry.Key)} key (handler names must be text)");
                continue;
            }

            if (!BehaviorEventNames.TryParse(name, out var kind)) {
                rejected.Add($"'{name}' (not an event name)");
                continue;
            }

            if (entry.Value is not LambdaMethod lambda) {
                rejected.Add($"'{name}' (is {Describe(entry.Value)}, not a function)");
                continue;
            }

            handlers[kind] = new BehaviorHandler(arguments => lambda.InvokeAsExecution(arguments!));
        }

        // An empty map is how a script says "I deliberately have no handlers"; a map whose every entry was
        // rejected is a mistake, and is the case worth being loud about.
        if (handlers.Count > 0 || rejected.Count == 0)
            return new CompiledBehavior(handlers);

        return Quarantined($"script declares no usable handlers -- rejected {string.Join(", ", rejected)}");
    }

    private static string Describe(object? value) => value switch {
        null => "nothing",
        string => "text",
        LambdaMethod => "a function",
        _ => $"a {value.GetType().Name}",
    };

    private static readonly IReadOnlyDictionary<BehaviorEventKind, BehaviorHandler> EmptyHandlers =
        new Dictionary<BehaviorEventKind, BehaviorHandler>();
}
