namespace Uberkarl.Packages;

/// <summary>
/// The read contract for discovering packages and enumerating/opening a package's contents,
/// independent of where the packages actually live. This is the seam a UI depends on instead of any
/// concrete storage: a local folder implements it today, a future online implementation drops in
/// behind the same contract with no change to a caller. No operation ever returns or requires a host
/// path or URL — <see cref="PackageHandle"/> is the only cross-boundary reference, and it is opaque.
/// </summary>
public interface IPackageSource
{
    /// <summary>
    /// Every valid package the source can currently offer, ordered by the source (stable, source-
    /// defined ordering). Never throws for an individual bad package — it is skipped instead — and may
    /// return an empty collection. Reads only manifests, never resource payloads.
    /// </summary>
    IReadOnlyList<PackageSummary> ListPackages();

    /// <summary>
    /// The resources listed in the manifest of the package <paramref name="handle"/> refers to. Opens
    /// and closes the package internally; holds no lingering handle. Throws
    /// <see cref="PackageUnavailableException"/> if the handle no longer resolves.
    /// </summary>
    IReadOnlyList<ResourceSummary> GetContents(PackageHandle handle);

    /// <summary>
    /// Opens the package <paramref name="handle"/> refers to for reading resource bytes. The caller
    /// owns disposal. Throws <see cref="PackageUnavailableException"/> if the handle no longer
    /// resolves.
    /// </summary>
    Package Open(PackageHandle handle);
}
