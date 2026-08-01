namespace Uberkarl.Packages;

public sealed class PackageManifest
{
    public int FormatVersion { get; init; } = PackageFormat.CurrentFormatVersion;

    public PackageId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public Attribution? Attribution { get; init; }

    public PackageId? ForkedFrom { get; init; }

    public IReadOnlyList<ResourceEntry> Resources { get; init; } = Array.Empty<ResourceEntry>();

    public IReadOnlyList<PackageDependency> Dependencies { get; init; } = Array.Empty<PackageDependency>();
}
