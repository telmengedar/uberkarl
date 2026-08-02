namespace Uberkarl.Packages;

/// <summary>
/// The write seam for a package source, kept separate from <see cref="IPackageSource"/> so a
/// read-only source (a future online one) is never forced to implement it. A local source implements
/// both; the split follows the interface-segregation shape the design calls for.
/// </summary>
public interface IWritablePackageSource
{
    /// <summary>
    /// Overwrites the package <paramref name="handle"/> refers to with <paramref name="packageBytes"/>
    /// — the target for a Save that overwrites the level's origin package. Throws
    /// <see cref="PackageUnavailableException"/> if the handle no longer resolves.
    /// </summary>
    void Write(PackageHandle handle, byte[] packageBytes);

    /// <summary>
    /// Creates a new package from <paramref name="packageBytes"/> under a name derived from
    /// <paramref name="proposedName"/>, returning its handle. Backs a future Save-As once a
    /// gamepad-friendly naming UI exists.
    /// </summary>
    PackageHandle Create(string proposedName, byte[] packageBytes);
}
