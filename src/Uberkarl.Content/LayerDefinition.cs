namespace Uberkarl.Content;

public sealed class LayerDefinition
{
    public const int EmptyCell = -1;

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The layer's role. Only <see cref="LayerRole.Main"/> honors tile collision;
    /// defaults to <see cref="LayerRole.Background"/> so pre-role levels stay display-only.
    /// </summary>
    public LayerRole Role { get; init; } = LayerRole.Background;

    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();
}
