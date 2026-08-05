namespace Uberkarl.Behavior;

/// <summary>
/// One live scripted subject registered with a <see cref="BehaviorScheduler"/> — the runtime pairing of a
/// subject id with its <see cref="CompiledBehavior"/> (design #7704 §6 "Runtime entities" — <c>BehaviorInstance</c>).
/// </summary>
public sealed class BehaviorInstance
{
    public BehaviorInstance(string subjectId, CompiledBehavior compiled)
    {
        SubjectId = subjectId;
        Compiled = compiled;
    }

    public string SubjectId { get; }

    public CompiledBehavior Compiled { get; }

    public bool IsQuarantined => Compiled.IsQuarantined;
}
