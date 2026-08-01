namespace Uberkarl.Content;

public sealed class LayerDefinition
{
    public const int EmptyCell = -1;

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();
}
