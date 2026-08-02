namespace Uberkarl.Packages;

/// <summary>
/// What a package source's package-selection step renders and selects. Built from a manifest without
/// loading any resource payload, so listing a source is cheap even for many packages.
/// </summary>
public sealed class PackageSummary
{
    public PackageId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public int ResourceCount { get; init; }

    public Attribution? Attribution { get; init; }

    /// <summary>The opaque locator to pass back to the source's <c>GetContents</c>/<c>Open</c>.</summary>
    public PackageHandle Handle { get; init; }
}
