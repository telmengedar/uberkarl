using Uberkarl.Packages;

namespace Uberkarl.Editor;

/// <summary>
/// One animation frame AFTER a tile's primary graphic (DiVoid #7551 Phase 2, design #7580) — the
/// authoring-side counterpart to <see cref="Content.TileDefinition.Frames"/>, holding the frame's bytes
/// in memory exactly like <see cref="EditableTile.Graphic"/> does for frame 0, so a tile set round-trips
/// without a live package handle.
/// </summary>
public sealed class EditableTileFrame
{
    public EditableTileFrame(ResourcePath graphicPath, byte[] graphic)
    {
        GraphicPath = graphicPath;
        Graphic = graphic ?? throw new ArgumentNullException(nameof(graphic));
    }

    /// <summary>The in-package resource path this frame's graphic is stored at (preserved on save).</summary>
    public ResourcePath GraphicPath { get; }

    /// <summary>The frame's graphic bytes (a PNG for the sample content).</summary>
    public byte[] Graphic { get; }
}
