using System.Text;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Derives the per-resource, namespaced in-package paths the package-as-VFS save model requires
/// (DiVoid #7571/#7572). Before this correction, <c>EditableLevel.DefaultLevelPath</c> ("levels/level.json")
/// and <c>DefaultTileSetPath</c> ("tileset.json") were fixed constants — a second level saved into the
/// same package would collide with the first, silently destroying it. Every resource a level owns is now
/// addressed under a path derived from a sanitized slug of the level's resource name: <c>levels/&lt;slug&gt;.json</c>,
/// <c>tilesets/&lt;slug&gt;.json</c>, <c>graphics/&lt;slug&gt;/&lt;tileId&gt;.png</c> — two distinctly-named levels in one
/// package can never collide.
/// </summary>
public static class LevelResourcePaths
{
    private const string LevelDirectory = "levels/";
    private const string LevelSuffix = ".json";

    /// <summary>
    /// Sanitizes <paramref name="name"/> into a lowercase, hyphenated path segment: letters/digits are
    /// kept, every run of anything else collapses to a single '-', and leading/trailing hyphens are
    /// trimmed. Falls back to <c>"level"</c> when the input has no alphanumeric content at all, so a
    /// slug is always a valid, non-empty <see cref="ResourcePath"/> segment.
    /// </summary>
    public static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "level";

        var builder = new StringBuilder(name.Length);
        var lastWasDash = false;
        foreach (var character in name.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        while (builder.Length > 0 && builder[^1] == '-')
            builder.Length--;

        return builder.Length == 0 ? "level" : builder.ToString();
    }

    /// <summary>The in-package path a level definition with this slug is stored at.</summary>
    public static ResourcePath LevelPath(string slug) => ResourcePath.Create($"{LevelDirectory}{slug}{LevelSuffix}");

    /// <summary>The in-package path a tile set with this slug is stored at.</summary>
    public static ResourcePath TileSetPath(string slug) => ResourcePath.Create($"tilesets/{slug}{LevelSuffix}");

    /// <summary>The in-package path a tile graphic with this slug and tile id is stored at.</summary>
    public static ResourcePath GraphicPath(string slug, int tileId) => ResourcePath.Create($"graphics/{slug}/{tileId}.png");

    /// <summary>
    /// Extracts the slug back out of a <c>levels/&lt;slug&gt;.json</c> path (the inverse of
    /// <see cref="LevelPath"/>), or <c>null</c> when <paramref name="path"/> does not follow that
    /// convention (e.g. a legacy fixed-constant path from before this correction). Used to keep an
    /// explicitly re-saved existing resource's sibling tileset/graphics on the same, already-established
    /// slug rather than re-deriving one from whatever the level is currently displayed as — a rename
    /// must never move a VFS entry (design #7572 open question 3).
    /// </summary>
    public static string? SlugFromLevelPath(ResourcePath path)
    {
        var value = path.Value;
        if (!value.StartsWith(LevelDirectory, StringComparison.Ordinal) || !value.EndsWith(LevelSuffix, StringComparison.Ordinal))
            return null;

        var slug = value[LevelDirectory.Length..^LevelSuffix.Length];
        return slug.Length == 0 ? null : slug;
    }

    /// <summary>
    /// Returns <paramref name="baseSlug"/> unchanged if <paramref name="isTaken"/> reports it free,
    /// otherwise appends <c>-2</c>, <c>-3</c>, … until a free slug is found. Enforces the "two levels in
    /// one package never collide" invariant when a brand-new level resource is being added.
    /// </summary>
    public static string UniqueSlug(string baseSlug, Func<string, bool> isTaken)
    {
        if (isTaken is null)
            throw new ArgumentNullException(nameof(isTaken));
        if (!isTaken(baseSlug))
            return baseSlug;

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseSlug}-{suffix}";
            suffix++;
        } while (isTaken(candidate));

        return candidate;
    }
}
