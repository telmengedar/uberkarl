using System.Text;

namespace Uberkarl.Packages;

/// <summary>
/// A package source over a single predefined, flat directory of <c>.pkg</c> files. The directory's
/// absolute path is handed in at construction — this type never knows about <c>res://</c>/<c>user://</c>
/// or any other engine path convention, that resolution happens in the caller. A <see cref="PackageHandle"/>
/// minted here carries the file's absolute path as its opaque token, but that token is never exposed
/// through any public member.
/// </summary>
public sealed class FolderPackageSource : IPackageSource, IWritablePackageSource
{
    private readonly string directory;

    public FolderPackageSource(string directory)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public IReadOnlyList<PackageSummary> ListPackages()
    {
        if (!Directory.Exists(directory))
            return Array.Empty<PackageSummary>();

        var summaries = new List<PackageSummary>();
        foreach (var path in Directory.EnumerateFiles(directory, "*" + PackageFormat.FileExtension, SearchOption.TopDirectoryOnly))
        {
            if (TryBuildSummary(path, out var summary))
                summaries.Add(summary);
        }

        summaries.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return summaries;
    }

    public IReadOnlyList<ResourceSummary> GetContents(PackageHandle handle)
    {
        using var package = OpenInternal(handle);

        var summaries = new List<ResourceSummary>(package.Manifest.Resources.Count);
        foreach (var entry in package.Manifest.Resources)
        {
            summaries.Add(new ResourceSummary
            {
                Path = entry.Path,
                Kind = entry.Kind,
                DisplayName = DisplayNameFor(entry.Path),
                MediaType = entry.MediaType,
                ByteLength = entry.ByteLength,
            });
        }

        return summaries;
    }

    public Package Open(PackageHandle handle) => OpenInternal(handle);

    public void Write(PackageHandle handle, byte[] packageBytes)
    {
        if (packageBytes is null)
            throw new ArgumentNullException(nameof(packageBytes));

        var path = handle.Token;
        if (!File.Exists(path))
            throw new PackageUnavailableException(path);

        File.WriteAllBytes(path, packageBytes);
    }

    public PackageHandle Create(string proposedName, byte[] packageBytes)
    {
        if (packageBytes is null)
            throw new ArgumentNullException(nameof(packageBytes));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, UniqueFileName(proposedName));
        File.WriteAllBytes(path, packageBytes);
        return PackageHandle.FromToken(path);
    }

    private static Package OpenInternal(PackageHandle handle)
    {
        var path = handle.Token;
        if (!File.Exists(path))
            throw new PackageUnavailableException(path);

        try
        {
            return PackageReader.Open(path);
        }
        catch (IOException exception)
        {
            throw new PackageUnavailableException(path, exception);
        }
    }

    // Owns the file stream itself (rather than the string-path overload, which opens one internally)
    // so a corrupt archive that throws out of the ZipArchive constructor still gets its handle released
    // here — listing must be able to skip a bad package repeatedly without leaking a handle to it.
    private static bool TryBuildSummary(string path, out PackageSummary summary)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var package = PackageReader.Open(stream, leaveOpen: true);
            summary = new PackageSummary
            {
                Id = package.Manifest.Id,
                Name = package.Manifest.Name,
                Version = package.Manifest.Version,
                ResourceCount = package.Manifest.Resources.Count,
                Attribution = package.Manifest.Attribution,
                Handle = PackageHandle.FromToken(path),
            };
            return true;
        }
        catch (PackageException)
        {
            summary = null!;
            return false;
        }
        catch (IOException)
        {
            summary = null!;
            return false;
        }
    }

    private string UniqueFileName(string proposedName)
    {
        var baseName = SanitizeFileName(proposedName);
        if (string.IsNullOrEmpty(baseName))
            baseName = "package";

        var candidate = baseName + PackageFormat.FileExtension;
        var suffix = 1;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{baseName}-{suffix}{PackageFormat.FileExtension}";
            suffix++;
        }

        return candidate;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);

        return builder.ToString().Trim();
    }

    private static string DisplayNameFor(ResourcePath path)
    {
        var value = path.Value;
        var slash = value.LastIndexOf('/');
        var fileName = slash >= 0 ? value[(slash + 1)..] : value;
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
