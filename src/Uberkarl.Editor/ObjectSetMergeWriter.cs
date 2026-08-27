using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>Builds the resource contributions the object sets a level references own on save.</summary>
public static class ObjectSetMergeWriter
{
    /// <summary>One object set's resource contributions — its definition, and each distinct object graphic.</summary>
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

    /// <summary>The contributions of every object set <paramref name="level"/>'s placements reference, read from <paramref name="package"/>, keyed on whether there is placement data to lose rather than on whether the level was ever attached: a null <paramref name="package"/> throws only when <paramref name="level"/>.Objects is non-empty.</summary>
    public static IReadOnlyList<PendingResource> BuildContributionsForLevel(Package? package, EditableLevel level)
    {
        if (level is null)
            throw new ArgumentNullException(nameof(level));
        if (package is null && level.Objects.Count > 0)
            throw new ArgumentNullException(nameof(package));
        if (package is null)
            return Array.Empty<PendingResource>();

        List<PendingResource> contributions = new List<PendingResource>();
        HashSet<ResourceReference> seen = new HashSet<ResourceReference>();
        foreach (EditableObjectPlacement placement in level.Objects)
        {
            ResourceReference reference = placement.Placement.ObjectSet;
            if (!seen.Add(reference))
                continue;

            IReadOnlyList<EditableObjectType> objectTypes = EditableObjectSetReader.FromPackage(package, reference);
            contributions.AddRange(BuildContributions(reference.Path, objectTypes));
        }

        return contributions;
    }
}
