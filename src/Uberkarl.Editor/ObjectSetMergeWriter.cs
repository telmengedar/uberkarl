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

        var contributions = new List<PendingResource>(objectTypes.Count + 1);

        var objectSetDefinition = new ObjectSetDefinition
        {
            Objects = objectTypes.Select(type => type.Definition).ToArray(),
        };
        contributions.Add(new PendingResource(
            objectSetPath, ResourceKind.ObjectSet, PackageFormat.DefaultMediaType,
            LevelContentSerializer.WriteObjectSet(objectSetDefinition), attribution: null));

        foreach (var type in objectTypes.DistinctBy(type => type.Definition.Graphic.Path))
            contributions.Add(new PendingResource(type.Definition.Graphic.Path, ResourceKind.Sprite, "image/png", type.Graphic, attribution: null));

        return contributions;
    }

    /// <summary>The contributions of every object set <paramref name="level"/>'s placements reference, read from <paramref name="package"/>.</summary>
    public static IReadOnlyList<PendingResource> BuildContributionsForLevel(Package package, EditableLevel level)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));
        if (level is null)
            throw new ArgumentNullException(nameof(level));

        var contributions = new List<PendingResource>();
        var seen = new HashSet<ResourceReference>();
        foreach (var placement in level.Objects)
        {
            var reference = placement.Placement.ObjectSet;
            if (!seen.Add(reference))
                continue;

            var objectTypes = EditableObjectSetReader.FromPackage(package, reference);
            contributions.AddRange(BuildContributions(reference.Path, objectTypes));
        }

        return contributions;
    }
}
