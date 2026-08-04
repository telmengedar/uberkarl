using System.Buffers.Binary;

namespace Uberkarl.Editor;

/// <summary>
/// Engine-agnostic sizing decisions for scaling a level-structure tile import to the grid's square size
/// (DiVoid #7551 bugfix, design #7580). <c>TileSetEditor.OnGraphicFileSelected</c> does the actual Godot
/// <c>Image.Resize</c> pixel work; this class only decides whether it's needed, so that part is testable
/// without the engine.
/// </summary>
public static class TileGraphicImport
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>
    /// True when a source image of <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> does
    /// not already match the square <paramref name="tileSize"/> and must be resized to fill the tile.
    /// </summary>
    public static bool NeedsResize(int sourceWidth, int sourceHeight, int tileSize) =>
        sourceWidth != tileSize || sourceHeight != tileSize;

    private const int IhdrWidthOffset = 16;
    private const int IhdrHeightOffset = 20;

    /// <summary>Reads width/height from a PNG's leading IHDR chunk. Returns <c>false</c> for anything not a well-formed PNG.</summary>
    public static bool TryReadSize(byte[] png, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (png is null || png.Length < 24)
            return false;

        for (var i = 0; i < Signature.Length; i++)
        {
            if (png[i] != Signature[i])
                return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(IhdrWidthOffset, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(IhdrHeightOffset, 4));
        return width > 0 && height > 0;
    }
}
