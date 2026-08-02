namespace Uberkarl.Packages;

/// <summary>
/// An opaque locator a source hands out for a package and takes back to resolve/open it. Only the
/// source that issued a handle can interpret it (the folder source's token is a file path; a future
/// online source's token would be an id or URL); no public member exposes that token, so a caller in
/// another assembly — the browser UI — can carry a handle around and compare it for equality without
/// ever learning where the package actually lives.
/// </summary>
public readonly struct PackageHandle : IEquatable<PackageHandle>
{
    private readonly string token;

    private PackageHandle(string token)
    {
        this.token = token;
    }

    internal static PackageHandle FromToken(string token) =>
        new(token ?? throw new ArgumentNullException(nameof(token)));

    internal string Token => token ?? throw new InvalidOperationException("Package handle is uninitialized.");

    public bool Equals(PackageHandle other) => string.Equals(token, other.token, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is PackageHandle other && Equals(other);

    public override int GetHashCode() => token is null ? 0 : StringComparer.Ordinal.GetHashCode(token);

    public static bool operator ==(PackageHandle left, PackageHandle right) => left.Equals(right);

    public static bool operator !=(PackageHandle left, PackageHandle right) => !left.Equals(right);
}
