using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// The archive-level merge primitives (DiVoid #7571/#7572's package-as-VFS correction) shared by every
/// resource-specific writer — <see cref="LevelMergeWriter"/> and <see cref="TileSetMergeWriter"/> (DiVoid
/// #7551 Phase 1a) alike. Composing a level's contributions and composing a tile set's contributions onto
/// an archive are the exact same operation (seed the identity + every sibling resource forward, add-or-
/// replace the contribution paths); only WHICH contributions differ per resource kind. Extracted so the
/// two writers cannot drift — a future resource kind (track/sprite/script, per design #7572's "generalizes"
/// note) reuses this without re-deriving it. This is also the outermost point every caller's contribution
/// list passes through before reaching <see cref="PackageBuilder"/>, so the list's own resource-path
/// invariant (<see cref="Reconcile"/>) is enforced here rather than by any individual caller.
/// </summary>
public static class PackageMergeWriter
{
    /// <summary>
    /// Merges <paramref name="contributions"/> onto <paramref name="existingPackage"/>: the result's
    /// identity is the existing package's, unchanged; every existing resource whose path is not among the
    /// contributions is carried forward byte-for-byte; contribution paths are added if new, replaced if
    /// already present. Byte-identical contributions at a shared path collapse to one; conflicting ones
    /// throw <see cref="LevelContentException"/>.
    /// </summary>
    public static byte[] Compose(Package existingPackage, IReadOnlyList<PendingResource> contributions)
    {
        if (existingPackage is null)
            throw new ArgumentNullException(nameof(existingPackage));
        if (contributions is null)
            throw new ArgumentNullException(nameof(contributions));

        var builder = new PackageBuilder().SeedFrom(existingPackage);
        foreach (PendingResource contribution in Reconcile(contributions))
            builder.AddOrReplaceResource(contribution.Kind, contribution.Path, contribution.Payload, contribution.MediaType, contribution.Attribution);

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Mints a brand-new archive containing only <paramref name="contributions"/> — a fresh
    /// <see cref="PackageId"/>, <paramref name="newPackageName"/> as the archive's display name, and the
    /// starter attribution the editor has always defaulted a freshly-created package to. Byte-identical
    /// contributions at a shared path collapse to one; conflicting ones throw <see cref="LevelContentException"/>.
    /// </summary>
    public static byte[] BuildFresh(string newPackageName, IReadOnlyList<PendingResource> contributions)
    {
        if (string.IsNullOrWhiteSpace(newPackageName))
            throw new ArgumentException("Package name must not be empty.", nameof(newPackageName));
        if (contributions is null)
            throw new ArgumentNullException(nameof(contributions));

        var builder = new PackageBuilder()
            .WithId(PackageId.New())
            .WithName(newPackageName)
            .WithVersion("0.1.0")
            .WithAttribution(new Attribution { Author = "Uberkarl", License = "CC0-1.0" });

        foreach (PendingResource contribution in Reconcile(contributions))
            builder.AddResource(contribution.Kind, contribution.Path, contribution.Payload, contribution.MediaType, contribution.Attribution);

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        return buffer.ToArray();
    }

    private static IEnumerable<PendingResource> Reconcile(IReadOnlyList<PendingResource> contributions)
    {
        return contributions
            .GroupBy(contribution => contribution.Path)
            .Select(group => group.Count() == 1 || IsIdenticalContent(group)
                ? group.First()
                : throw new LevelContentException(
                    $"Resource path '{group.Key}' is contributed with conflicting content ({string.Join(", ", group.Select(contribution => contribution.Kind).Distinct())})."));
    }

    private static bool IsIdenticalContent(IEnumerable<PendingResource> group)
    {
        PendingResource first = group.First();
        return group.All(contribution => contribution.MediaType == first.MediaType && contribution.Payload.SequenceEqual(first.Payload));
    }
}
