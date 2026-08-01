using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Packages.Tests;

[TestFixture]
public sealed class ReaderValidationTests
{
    [Test]
    public void Open_RejectsNewerFormatVersion()
    {
        var manifest = $"{{\"formatVersion\":999,\"id\":\"{Guid.NewGuid():D}\",\"name\":\"future\",\"version\":\"1.0.0\",\"resources\":[],\"dependencies\":[]}}";
        using var buffer = ArchiveWith(PackageFormat.ManifestEntryName, manifest);

        Assert.That(() => PackageReader.Open(buffer, leaveOpen: true), Throws.TypeOf<PackageFormatException>());
    }

    [Test]
    public void Open_RejectsArchiveWithoutManifest()
    {
        using var buffer = ArchiveWith("resources/stray", "no manifest here");

        Assert.That(() => PackageReader.Open(buffer, leaveOpen: true), Throws.TypeOf<PackageFormatException>());
    }

    [Test]
    public void Open_RejectsManifestReferencingMissingPayload()
    {
        var manifest =
            $"{{\"formatVersion\":1,\"id\":\"{Guid.NewGuid():D}\",\"name\":\"broken\",\"version\":\"1.0.0\"," +
            "\"resources\":[{\"path\":\"sprites/ghost\",\"kind\":\"sprite\",\"mediaType\":\"image/png\",\"byteLength\":3}]," +
            "\"dependencies\":[]}";
        using var buffer = ArchiveWith(PackageFormat.ManifestEntryName, manifest);

        Assert.That(() => PackageReader.Open(buffer, leaveOpen: true), Throws.TypeOf<PackageFormatException>());
    }

    [Test]
    public void Open_RejectsNonArchiveInput()
    {
        using var buffer = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip"));

        Assert.That(() => PackageReader.Open(buffer, leaveOpen: true), Throws.TypeOf<PackageFormatException>());
    }

    private static MemoryStream ArchiveWith(string entryName, string content)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }

        buffer.Position = 0;
        return buffer;
    }
}
