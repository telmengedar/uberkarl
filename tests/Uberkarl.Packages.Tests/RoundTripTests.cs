using System.Text;
using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Packages.Tests;

[TestFixture]
public sealed class RoundTripTests
{
    [Test]
    public void WriteReadResolve_ReturnsOriginalResourceBytes()
    {
        var spritePath = ResourcePath.Create("sprites/hero");
        var spriteBytes = Encoding.UTF8.GetBytes("PNGDATA-hero");

        var builder = new PackageBuilder()
            .WithName("Forest Pack")
            .WithVersion("1.2.0")
            .WithAttribution(new Attribution { Author = "Toni", License = "CC-BY-4.0" });
        builder.AddResource(ResourceKind.Sprite, spritePath, spriteBytes, "image/png");

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;

        using var package = PackageReader.Open(buffer, leaveOpen: true);
        var resolved = package.Resolve(ResourceReference.ToSelf(spritePath));

        Assert.That(resolved, Is.EqualTo(spriteBytes));
    }

    [Test]
    public void WriteRead_PreservesManifestMetadataAndVersions()
    {
        var id = PackageId.New();
        var builder = new PackageBuilder()
            .WithId(id)
            .WithName("Forest Pack")
            .WithVersion("1.2.0")
            .WithAttribution(new Attribution { Author = "Toni", License = "CC-BY-4.0", Source = "example.test/forest" });
        builder.AddResource(ResourceKind.Script, ResourcePath.Create("scripts/hero-behavior"), Encoding.UTF8.GetBytes("on collide jump"));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;

        using var package = PackageReader.Open(buffer, leaveOpen: true);
        var manifest = package.Manifest;

        Assert.Multiple(() =>
        {
            Assert.That(manifest.FormatVersion, Is.EqualTo(PackageFormat.CurrentFormatVersion));
            Assert.That(manifest.Id, Is.EqualTo(id));
            Assert.That(manifest.Name, Is.EqualTo("Forest Pack"));
            Assert.That(manifest.Version, Is.EqualTo("1.2.0"));
            Assert.That(manifest.Attribution!.Author, Is.EqualTo("Toni"));
            Assert.That(manifest.Attribution!.License, Is.EqualTo("CC-BY-4.0"));
            Assert.That(manifest.Attribution!.Source, Is.EqualTo("example.test/forest"));
        });
    }

    [Test]
    public void License_IsStoredAsResource_AndAttributionPointsAtIt()
    {
        var licensePath = ResourcePath.Create("license");
        var builder = new PackageBuilder().WithName("Forest Pack");
        builder.AddLicense(licensePath, "CC-BY-4.0", Encoding.UTF8.GetBytes("Creative Commons Attribution 4.0"));

        using var buffer = new MemoryStream();
        builder.Write(buffer);
        buffer.Position = 0;

        using var package = PackageReader.Open(buffer, leaveOpen: true);
        var licenseEntry = package.GetEntry(licensePath);
        var licenseBytes = package.ReadBytes(licensePath);

        Assert.Multiple(() =>
        {
            Assert.That(licenseEntry.Kind, Is.EqualTo(ResourceKind.License));
            Assert.That(Encoding.UTF8.GetString(licenseBytes), Is.EqualTo("Creative Commons Attribution 4.0"));
            Assert.That(package.Manifest.Attribution!.License, Is.EqualTo("CC-BY-4.0"));
            Assert.That(package.Manifest.Attribution!.LicenseResource, Is.EqualTo(licensePath));
        });
    }

    [Test]
    public void WriteToFile_RoundTripsFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"uberkarl-{Guid.NewGuid():N}{PackageFormat.FileExtension}");
        var resourcePath = ResourcePath.Create("tracks/level1-theme");
        var payload = Encoding.UTF8.GetBytes("TRACKDATA");

        try
        {
            var builder = new PackageBuilder().WithName("Music Pack");
            builder.AddResource(ResourceKind.Track, resourcePath, payload);
            builder.Write(path);

            using var package = PackageReader.Open(path);
            Assert.That(package.ReadBytes(resourcePath), Is.EqualTo(payload));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
