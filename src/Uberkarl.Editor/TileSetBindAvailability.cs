using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Engine-agnostic decision logic behind <c>TileSetBindPanel</c>'s "always open, always give feedback"
/// rule (DiVoid #7551 bugfix): whether it can browse a package's siblings at all, and if so, which of
/// that package's resources are other bindable tile sets. The panel only renders what this decides.
/// </summary>
public static class TileSetBindAvailability
{
    /// <summary>
    /// The message the bind panel should show INSTEAD of a sibling list when it cannot browse a package
    /// at all — <c>null</c> when it can (a normal sibling listing applies, empty or not).
    /// </summary>
    public static string? UnavailableReason(bool hasSession, bool hasPackageContext)
    {
        if (!hasSession)
            return "No level is open.";
        if (!hasPackageContext)
            return "This level isn't saved to a package yet — save it first, then Bind Tileset can list other tile sets in that package.";
        return null;
    }

    /// <summary>
    /// Every OTHER <c>tileset</c> resource in a package's contents — the level's currently-bound one
    /// excluded — in encounter order. Legitimately empty (never null) when the package has no siblings;
    /// the panel's own empty-state message covers that case, this method only decides membership.
    /// </summary>
    public static List<ResourceSummary> SelectBindableSiblings(IEnumerable<ResourceSummary> packageContents, ResourceReference current)
    {
        if (packageContents is null)
            throw new ArgumentNullException(nameof(packageContents));

        var found = new List<ResourceSummary>();
        foreach (var entry in packageContents)
        {
            if (entry.Kind == ResourceKind.TileSet && !(current.IsSelf && entry.Path == current.Path))
                found.Add(entry);
        }

        return found;
    }
}
