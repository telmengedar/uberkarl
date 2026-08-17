namespace Uberkarl.Behavior.Json;

using System.Text.Json;

/// <summary>
/// Decodes the author-facing scalar values (string/number/bool/null) used by both
/// <see cref="BehaviorBindingJsonConverter"/>'s <c>params</c> and <see cref="BehaviorStateJsonConverter"/>'s
/// state maps — a raw <see cref="JsonElement"/> into the matching CLR primitive, so neither converter leaks
/// <see cref="JsonElement"/> into script-facing data. Writing needs no counterpart: by write time these
/// values are already concrete CLR primitives that <see cref="JsonSerializer"/> serializes natively.
/// </summary>
internal static class JsonScalarValue
{
    public static object? Read(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new JsonException($"Unsupported scalar value kind '{element.ValueKind}'."),
    };
}
