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

        if (!PredefinedBehaviors.TryGetSource(binding.PredefinedId!, binding.Parameters, out var source))
            return Quarantined($"unknown predefined behavior id '{binding.PredefinedId}'");

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

        return new CompiledBehavior(ExtractHandlers(initResult));
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
