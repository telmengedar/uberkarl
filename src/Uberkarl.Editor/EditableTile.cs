using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// A single tile in the level's palette as the editor holds it: its numeric id (what a grid cell
/// stores), the in-package path its graphic lives at, the raw graphic bytes (kept in memory so the
/// level can be re-saved without a live package handle), and whether it is solid. This is the
/// authoring shape — distinct from <see cref="Content.TileDefinition"/> (which references the graphic
/// but does not carry its bytes) and from the resolved runtime view.
/// </summary>
public sealed class EditableTile
{
    public EditableTile(int id, ResourcePath graphicPath, byte[] graphic, bool collides, string? name = null)
    {
        if (id == Content.LayerDefinition.EmptyCell)
            throw new ArgumentException($"Tile id {Content.LayerDefinition.EmptyCell} is reserved for empty cells.", nameof(id));
        Id = id;
        GraphicPath = graphicPath;
        Graphic = graphic ?? throw new ArgumentNullException(nameof(graphic));
        Collides = collides;
        Name = name;
    }

    /// <summary>The numeric id a grid cell stores to place this tile.</summary>
    public int Id { get; }

    /// <summary>Optional author-facing display name (DiVoid #7551 — named via the on-screen keyboard).</summary>
    public string? Name { get; }

    /// <summary>The in-package resource path this tile's graphic is stored at (preserved on save).</summary>
    public ResourcePath GraphicPath { get; }

    /// <summary>The tile graphic bytes (a PNG for the sample content). Held so the tile set round-trips without the source package.</summary>
    public byte[] Graphic { get; }

    /// <summary>Whether the tile is solid. Only enforced on a collision layer at play time.</summary>
    public bool Collides { get; }
}
