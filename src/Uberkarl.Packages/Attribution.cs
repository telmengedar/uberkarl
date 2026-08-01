namespace Uberkarl.Packages;

public sealed class Attribution
{
    public string? Author { get; init; }

    public string? License { get; init; }

    public ResourcePath? LicenseResource { get; init; }

    public string? Source { get; init; }

    public string? Notes { get; init; }
}
