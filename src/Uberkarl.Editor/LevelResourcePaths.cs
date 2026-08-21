using System.Text;
using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// Derives the per-resource, namespaced in-package path a level owns (the package-as-VFS save model,
/// DiVoid #7571/#7572). Before that correction, <c>EditableLevel.DefaultLevelPath</c> ("levels/level.json")
/// was a fixed constant — a second level saved into the same package would collide with the first,
/// silently destroying it. A level's own resource is now addressed under a path derived from a sanitized
/// slug of the level's resource name: <c>levels/&lt;slug&gt;.json</c> — two distinctly-named levels in one
/// package can never collide.
///
/// <b>Shared-tileset correction (DiVoid #7551 Phase 1a):</b> this class used to ALSO derive a level-owned
/// tile set/graphic path scheme (<c>tilesets/&lt;slug&gt;.json</c>, <c>graphics/&lt;slug&gt;/&lt;tileId&gt;.png</c>) —
/// removed here, because a tile set is no longer a level-owned resource. The equivalent scheme for a
/// standalone SHARED tile set now lives on <see cref="TileSetResourcePaths"/>, addressed by the tile
/// set's OWN name, independent of any level.
/// </summary>
public static class LevelResourcePaths
{
    private const string LevelDirectory = "levels/";
    private const string LevelSuffix = ".json";

    /// <summary>Sanitizes <paramref name="name"/> into a per-character-lowercased, hyphenated path segment, falling back to <paramref name="fallback"/> for empty input.</summary>
    public static string Slugify(string name, string fallback = "level")
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

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

        return builder.Length == 0 ? fallback : builder.ToString();
    }

    /// <summary>The in-package path a level definition with this slug is stored at.</summary>
    public static ResourcePath LevelPath(string slug) => ResourcePath.Create($"{LevelDirectory}{slug}{LevelSuffix}");

    // TileSetPath/GraphicPath used to live here too — removed under the shared-tileset correction (DiVoid
    // #7551 Phase 1a): a tile set is no longer a level-owned, level-namespaced resource, so its path
    // scheme moved to its own type, TileSetResourcePaths (mirrors this class's shape exactly).

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
