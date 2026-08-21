namespace Uberkarl.Behavior;

/// <summary>
/// One entry of the predefined behavior library as authoring metadata (design #7704 §10.5 / #8049 §6.2):
/// its stable id, an author-facing label, which subject kinds it may legally be bound to, and its tunable
/// parameters. <see cref="PredefinedBehaviors.Descriptors"/> is the single source of truth this reads from
/// and <see cref="PredefinedBehaviors.TryGetSource"/> also reads its defaults from.
/// </summary>
public sealed class PredefinedBehaviorDescriptor
{
    public PredefinedBehaviorDescriptor(string id, string label, IReadOnlyList<BehaviorSubjectKind> applicableKinds, IReadOnlyList<PredefinedParameterDescriptor> parameters)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        ApplicableKinds = applicableKinds ?? throw new ArgumentNullException(nameof(applicableKinds));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    /// <summary>The stable predefined id (matches a <see cref="PredefinedBehaviors"/> id constant).</summary>
    public string Id { get; }

    /// <summary>The author-facing name shown in the assignment picker.</summary>
    public string Label { get; }

    /// <summary>The subject kinds this predefined may be bound to.</summary>
    public IReadOnlyList<BehaviorSubjectKind> ApplicableKinds { get; }

    /// <summary>This predefined's tunable parameters, in the order the assignment picker steps through them.</summary>
    public IReadOnlyList<PredefinedParameterDescriptor> Parameters { get; }

    /// <summary>Whether this predefined may be bound to a subject of <paramref name="kind"/>.</summary>
    public bool AppliesTo(BehaviorSubjectKind kind) => ApplicableKinds.Contains(kind);

    /// <summary>The parameter descriptor named <paramref name="name"/>. Throws if none matches.</summary>
    public PredefinedParameterDescriptor Parameter(string name) => Parameters.First(parameter => parameter.Name == name);
}
