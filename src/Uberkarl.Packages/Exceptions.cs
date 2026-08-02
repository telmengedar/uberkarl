namespace Uberkarl.Packages;

public class PackageException : Exception
{
    public PackageException(string message)
        : base(message)
    {
    }

    public PackageException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public sealed class PackageFormatException : PackageException
{
    public PackageFormatException(string message)
        : base(message)
    {
    }

    public PackageFormatException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public sealed class ResourceNotFoundException : PackageException
{
    public ResourceNotFoundException(ResourcePath path)
        : base($"Resource '{path}' was not found in the package.")
    {
        Path = path;
    }

    public ResourcePath Path { get; }
}

public sealed class UnresolvedReferenceException : PackageException
{
    public UnresolvedReferenceException(ResourceReference reference, string reason)
        : base($"Reference '{reference}' could not be resolved: {reason}")
    {
        Reference = reference;
    }

    public ResourceReference Reference { get; }
}

public sealed class PackageUnavailableException : PackageException
{
    public PackageUnavailableException(string locator)
        : base($"Package '{locator}' is no longer available.")
    {
    }

    public PackageUnavailableException(string locator, Exception inner)
        : base($"Package '{locator}' is no longer available.", inner)
    {
    }
}
