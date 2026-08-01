using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Uberkarl.Tools.SampleContent;

internal static class PngWriter
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write(Signature, 0, Signature.Length);
        WriteChunk(output, "IHDR", BuildHeader(width, height));
        WriteChunk(output, "IDAT", Compress(AddFilterBytes(width, height, rgba)));
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static byte[] BuildHeader(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        return header;
    }

    private static byte[] AddFilterBytes(int width, int height, byte[] rgba)
    {
        var stride = width * 4;
        var filtered = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var source = y * stride;
            var destination = y * (stride + 1);
            filtered[destination] = 0;
            Array.Copy(rgba, source, filtered, destination + 1, stride);
        }

        return filtered;
    }

    private static byte[] Compress(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return buffer.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length, 0, 4);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes, 0, typeBytes.Length);
        output.Write(data, 0, data.Length);

        var crc = Crc(typeBytes, data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes, 0, 4);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        crc = Update(crc, type);
        crc = Update(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Update(uint crc, byte[] bytes)
    {
        foreach (var value in bytes)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
