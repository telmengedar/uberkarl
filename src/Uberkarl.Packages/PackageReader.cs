using System.IO.Compression;
using Uberkarl.Packages.Json;

namespace Uberkarl.Packages;

public static class PackageReader
{
    public static Package Open(string path)
    {
        var stream = File.OpenRead(path);
        return Open(stream, leaveOpen: false);
    }

    public static Package Open(Stream stream, bool leaveOpen = false)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        }
        catch (InvalidDataException exception)
        {
            throw new PackageFormatException("Package is not a valid archive.", exception);
        }

        try
        {
            GuardAgainstTraversal(archive);

            var manifestEntry = archive.GetEntry(PackageFormat.ManifestEntryName)
                ?? throw new PackageFormatException("Package is missing its manifest.");

            PackageManifest manifest;
            using (var manifestStream = manifestEntry.Open())
                manifest = ManifestSerializer.Read(manifestStream);

            if (manifest.FormatVersion > PackageFormat.CurrentFormatVersion)
                throw new PackageFormatException(
                    $"Package format version {manifest.FormatVersion} is newer than the supported version {PackageFormat.CurrentFormatVersion}.");

            ValidatePayloads(archive, manifest);

            return new Package(manifest, archive);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private static void GuardAgainstTraversal(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.Contains("..", StringComparison.Ordinal) || name.StartsWith('/') || name.Contains('\\'))
                throw new PackageFormatException($"Package contains an unsafe entry path '{name}'.");
        }
    }

    private static void ValidatePayloads(ZipArchive archive, PackageManifest manifest)
    {
        foreach (var entry in manifest.Resources)
        {
            if (archive.GetEntry(Package.PayloadEntryName(entry.Path)) is null)
                throw new PackageFormatException($"Manifest lists resource '{entry.Path}' but its payload is missing.");
        }
    }
}
