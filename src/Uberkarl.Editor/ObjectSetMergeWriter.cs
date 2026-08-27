using Uberkarl.Content;
using Uberkarl.Content.Json;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>Builds the resource contributions an object set owns on save: its definition and every object type's graphic.</summary>
public static class ObjectSetMergeWriter
{
    /// <summary>
    /// The object set's resource contributions — its definition at <paramref name="objectSetPath"/>, and
    /// each object type's graphic at the <see cref="Uberkarl.Packages.ResourceReference"/> the type
    /// already carries. Pure — no IO, no knowledge of any archive this might be merged into.
    /// </summary>
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

        foreach (var type in objectTypes)
            contributions.Add(new PendingResource(type.Definition.Graphic.Path, ResourceKind.Sprite, "image/png", type.Graphic, attribution: null));

        return contributions;
    }

    /// <summary>Merges <paramref name="contributions"/> onto <paramref name="existingPackage"/>. Delegates to <see cref="PackageMergeWriter.Compose"/>.</summary>
    public static byte[] Compose(Package existingPackage, IReadOnlyList<PendingResource> contributions)
        => PackageMergeWriter.Compose(existingPackage, contributions);

    /// <summary>Mints a brand-new archive containing only <paramref name="contributions"/>. Delegates to <see cref="PackageMergeWriter.BuildFresh"/>.</summary>
    public static byte[] BuildFresh(string newPackageName, IReadOnlyList<PendingResource> contributions)
        => PackageMergeWriter.BuildFresh(newPackageName, contributions);
}
