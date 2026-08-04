using NUnit.Framework;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers <see cref="TileGraphicImport"/> — the engine-agnostic sizing decision behind the "imported PNGs must be tile-sized" bugfix (DiVoid #7551).</summary>
[TestFixture]
public sealed class TileGraphicImportTests
{
    [Test]
    public void NeedsResize_NativeSizeMatchesTileSize_ReturnsFalse()
    {
        Assert.That(TileGraphicImport.NeedsResize(16, 16, 16), Is.False);
    }

    [Test]
    public void NeedsResize_LargerNativeSize_ReturnsTrue()
    {
        Assert.That(TileGraphicImport.NeedsResize(512, 512, 16), Is.True);
    }

    [Test]
    public void NeedsResize_SmallerNativeSize_ReturnsTrue()
    {
        Assert.That(TileGraphicImport.NeedsResize(8, 8, 16), Is.True);
    }

    [Test]
    public void NeedsResize_NonSquareNativeSize_ReturnsTrue()
    {
        Assert.That(TileGraphicImport.NeedsResize(16, 32, 16), Is.True);
    }

    [Test]
    public void NeedsResize_LargeTileGrid_MatchingNativeSize_ReturnsFalse()
    {
        Assert.That(TileGraphicImport.NeedsResize(64, 64, 64), Is.False);
    }

    [Test]
    public void NeedsResize_WidthDiffersButHeightMatches_ReturnsTrue()
    {
        Assert.That(TileGraphicImport.NeedsResize(8, 16, 16), Is.True);
    }

    [Test]
    public void TryReadSize_ReadsWidthAndHeight_FromAWellFormedPngHeader()
    {
        byte[] png = MakePngHeader(512, 300);

        bool ok = TileGraphicImport.TryReadSize(png, out int width, out int height);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(width, Is.EqualTo(512));
            Assert.That(height, Is.EqualTo(300));
        });
    }

    [Test]
    public void TryReadSize_TooShortToContainAHeader_ReturnsFalse()
    {
        byte[] tooShort = { 137, 80, 78, 71, 13, 10, 26, 10 };

        bool ok = TileGraphicImport.TryReadSize(tooShort, out int width, out int height);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(width, Is.EqualTo(0));
            Assert.That(height, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryReadSize_WrongSignature_ReturnsFalse()
    {
        byte[] notPng = new byte[24];

        Assert.That(TileGraphicImport.TryReadSize(notPng, out _, out _), Is.False);
    }

    [Test]
    public void TryReadSize_SignatureDivergesOnALaterByte_ReturnsFalse()
    {
        byte[] png = MakePngHeader(16, 16);
        png[7] = 0;

        Assert.That(TileGraphicImport.TryReadSize(png, out _, out _), Is.False);
    }

    [Test]
    public void TryReadSize_ZeroHeight_ReturnsFalse()
    {
        byte[] png = MakePngHeader(16, 0);

        Assert.That(TileGraphicImport.TryReadSize(png, out _, out _), Is.False);
    }

    [Test]
    public void TryReadSize_ZeroWidth_ReturnsFalse()
    {
        byte[] png = MakePngHeader(0, 16);

        Assert.That(TileGraphicImport.TryReadSize(png, out _, out _), Is.False);
    }

    [Test]
    public void TryReadSize_NullBuffer_ReturnsFalse()
    {
        Assert.That(TileGraphicImport.TryReadSize(null!, out _, out _), Is.False);
    }

    private static byte[] MakePngHeader(int width, int height)
    {
        byte[] png = new byte[24];
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        Array.Copy(signature, png, signature.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), height);
        return png;
    }
}
