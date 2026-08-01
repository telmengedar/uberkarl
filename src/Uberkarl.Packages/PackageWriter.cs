using System.IO.Compression;
using Uberkarl.Packages.Json;

namespace Uberkarl.Packages;

internal static class PackageWriter
{
    public static void Write(PackageBuilder builder, Stream destination)
    {
        var manifest = builder.BuildManifest();

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        var manifestEntry = archive.CreateEntry(PackageFormat.ManifestEntryName, CompressionLevel.Optimal);
        using (var manifestStream = manifestEntry.Open())
            ManifestSerializer.Write(manifestStream, manifest);

        foreach (var resource in Ordered(builder.Resources))
        {
            var payloadEntry = archive.CreateEntry(Package.PayloadEntryName(resource.Path), CompressionLevel.Optimal);
            using var payloadStream = payloadEntry.Open();
            payloadStream.Write(resource.Payload, 0, resource.Payload.Length);
        }
    }

    private static IEnumerable<PendingResource> Ordered(IReadOnlyList<PendingResource> resources)
        => resources.OrderBy(resource => resource.Path.Value, StringComparer.Ordinal);
}
