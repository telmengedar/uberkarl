namespace Uberkarl.Packages;

/// <summary>
/// Resolves a proposed package name typed for a "+ New package…" Save-As entry against the packages a
/// source already lists (DiVoid #7552's confirm-overwrite safety net). <see cref="FolderPackageSource.Create"/>
/// would otherwise silently mint a distinct, suffixed file (<c>name-1.pkg</c>) next to an existing package
/// of the same display name — surprising for an author who typed a name expecting to land on "the" package
/// by that name. This resolver decides, in one pure and engine-agnostic place, whether that should instead
/// be treated as an overwrite of the existing package.
/// </summary>
public static class PackageSaveTargetResolver
{
    /// <summary>
    /// The handle of the existing package whose <see cref="PackageSummary.Name"/> matches
    /// <paramref name="proposedName"/> (trimmed, ordinal-case-insensitive), or <c>null</c> when no
    /// existing package shares that name and <paramref name="proposedName"/> is safe to create fresh.
    /// </summary>
    public static PackageHandle? FindCollision(IReadOnlyList<PackageSummary> packages, string proposedName)
    {
        if (packages is null)
            throw new ArgumentNullException(nameof(packages));
        if (string.IsNullOrWhiteSpace(proposedName))
            return null;

        var trimmed = proposedName.Trim();
        foreach (var package in packages)
        {
            if (string.Equals(package.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                return package.Handle;
        }

        return null;
    }
}
