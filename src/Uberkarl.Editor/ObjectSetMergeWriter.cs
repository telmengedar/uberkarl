using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>Builds the resource contributions the object sets a level references own on save.</summary>
public static class ObjectSetMergeWriter
{
    /// <summary>One object set's resource contributions — its definition, and each object type's graphic.</summary>
    public static IReadOnlyList<PendingResource> BuildContributions(ResourcePath objectSetPath, IReadOnlyList<EditableObjectType> objectTypes)
    {
        if (objectTypes is null)
            throw new ArgumentNullException(nameof(objectTypes));

        List<PendingResource> contributions = new List<PendingResource>(objectTypes.Count + 1);

        ObjectSetDefinition objectSetDefinition = new ObjectSetDefinition
        {
            Objects = objectTypes.Select(type => type.Definition).ToArray(),
        };
        contributions.Add(new PendingResource(
            objectSetPath, ResourceKind.ObjectSet, PackageFormat.DefaultMediaType,
            LevelContentSerializer.WriteObjectSet(objectSetDefinition), attribution: null));

        foreach (EditableObjectType type in objectTypes)
            contributions.Add(new PendingResource(type.Definition.Graphic.Path, ResourceKind.Sprite, "image/png", type.Graphic, attribution: null));

        return contributions;
    }

    /// <summary>The contributions of every object set <paramref name="level"/>'s placements reference, read from <paramref name="package"/>, excluding <paramref name="authored"/>. A null <paramref name="package"/> throws only when an unseeded set is actually needed.</summary>
    public static IReadOnlyList<PendingResource> BuildContributionsForLevel(Package? package, EditableLevel level, ResourceReference? authored)
    {
        if (level is null)
            throw new ArgumentNullException(nameof(level));

        List<PendingResource> contributions = new List<PendingResource>();
        HashSet<ResourceReference> seen = new HashSet<ResourceReference>();
        if (authored is { } authoredReference)
            seen.Add(authoredReference);

        foreach (EditableObjectPlacement placement in level.Objects)
        {
            ResourceReference reference = placement.Placement.ObjectSet;
            if (!seen.Add(reference))
                continue;

            if (package is null)
                throw new ArgumentNullException(nameof(package));

            IReadOnlyList<EditableObjectType> objectTypes = EditableObjectSetReader.FromPackage(package, reference);
            contributions.AddRange(BuildContributions(reference.Path, objectTypes));
        }

        return contributions;
    }
}
