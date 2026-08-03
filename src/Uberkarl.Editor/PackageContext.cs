using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// The archive an editor session is currently working in — its identity (the fields that used to live,
/// wrongly, on <see cref="EditableLevel"/>) plus its resource inventory (manifest entries: path/kind/
/// media type/size, never payloads). This is the de-conflation the package-as-VFS correction (DiVoid
/// #7571/#7572) introduces: a package is an archive of many typed resources, and the editor must hold
/// that archive's identity separately from whichever one level resource happens to be open on the
/// canvas. Engine-agnostic (no Godot types) — the Godot glue (<c>LevelEditor</c>) retains one of these
/// across load/save so Save can merge into it instead of the old writer's "fabricate a whole package
/// around this one level."
/// </summary>
public sealed class PackageContext
{
    public PackageContext(
        PackageId id,
        string name,
        string version,
        Attribution? attribution,
        PackageId? forkedFrom,
        IReadOnlyList<PackageDependency> dependencies,
        PackageHandle handle,
        IReadOnlyList<ResourceEntry> resources)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Attribution = attribution;
        ForkedFrom = forkedFrom;
        Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        Handle = handle;
        Resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    /// <summary>The archive's own identity — independent of any level's display name.</summary>
    public PackageId Id { get; }

    /// <summary>The archive's display name, set at ＋New-package time; never derived from a level.</summary>
    public string Name { get; }

    public string Version { get; }

    public Attribution? Attribution { get; }

    public PackageId? ForkedFrom { get; }

    public IReadOnlyList<PackageDependency> Dependencies { get; }

    /// <summary>The opaque locator this archive currently lives at, for re-opening on the next Save.</summary>
    public PackageHandle Handle { get; }

    /// <summary>Every resource entry currently in the archive's manifest (path/kind/mediaType/size only).</summary>
    public IReadOnlyList<ResourceEntry> Resources { get; }

    /// <summary>Whether the archive already has a resource at <paramref name="path"/>.</summary>
    public bool Contains(ResourcePath path)
    {
        foreach (var entry in Resources)
        {
            if (entry.Path == path)
                return true;
        }

        return false;
    }

    /// <summary>Builds a context from an opened package and the handle it was opened through.</summary>
    public static PackageContext FromPackage(Package package, PackageHandle handle)
    {
        if (package is null)
            throw new ArgumentNullException(nameof(package));

        var manifest = package.Manifest;
        return new PackageContext(
            manifest.Id,
            manifest.Name,
            manifest.Version,
            manifest.Attribution,
            manifest.ForkedFrom,
            manifest.Dependencies,
            handle,
            manifest.Resources);
    }
}
