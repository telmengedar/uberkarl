namespace Uberkarl.Packages;

public sealed class PackageDependency
{
    public PackageId Package { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;
}
