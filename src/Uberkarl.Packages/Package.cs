using System.IO.Compression;

namespace Uberkarl.Packages;

public sealed class Package : IDisposable
{
    private readonly ZipArchive archive;
    private readonly Dictionary<string, ResourceEntry> entriesByPath;

    internal Package(PackageManifest manifest, ZipArchive archive)
    {
        Manifest = manifest;
        this.archive = archive;
        entriesByPath = new Dictionary<string, ResourceEntry>(StringComparer.Ordinal);
        foreach (var entry in manifest.Resources)
            entriesByPath[entry.Path.Value] = entry;
    }

    public PackageManifest Manifest { get; }

    public PackageId Id => Manifest.Id;

    public bool Contains(ResourcePath path) => entriesByPath.ContainsKey(path.Value);

    public ResourceEntry GetEntry(ResourcePath path)
    {
        if (!entriesByPath.TryGetValue(path.Value, out var entry))
            throw new ResourceNotFoundException(path);
        return entry;
    }

    public byte[] ReadBytes(ResourcePath path)
    {
        if (!entriesByPath.ContainsKey(path.Value))
            throw new ResourceNotFoundException(path);

        var zipEntry = archive.GetEntry(PayloadEntryName(path));
        if (zipEntry is null)
            throw new PackageFormatException($"Payload for resource '{path}' is missing from the package.");

        using var source = zipEntry.Open();
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    public byte[] Resolve(ResourceReference reference)
    {
        if (!reference.IsSelf && reference.Package != Id)
            throw new UnresolvedReferenceException(reference, "reference targets a different package.");
        return ReadBytes(reference.Path);
    }

    internal static string PayloadEntryName(ResourcePath path) => PackageFormat.ResourceRoot + path.Value;

    public void Dispose() => archive.Dispose();
}
