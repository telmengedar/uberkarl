using System.Globalization;

namespace Uberkarl.Content;

/// <summary>
/// An engine-agnostic RGBA colour (8 bits per channel). Authored in level content as a hex string
/// (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>, with or without the leading <c>#</c>) and parsed to this
/// value type at the Godot-free loader boundary, so a malformed colour fails loudly at load rather
/// than in the engine. The game layer maps it onto its own colour type.
/// </summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A)
{
    /// <summary>Fully opaque alpha, used when a 6-digit hex string omits the alpha channel.</summary>
    public const byte OpaqueAlpha = 255;

    /// <summary>
    /// Parses a hex colour string (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>, leading <c>#</c> optional).
    /// A 6-digit value is fully opaque. Returns <c>false</c> for any other length or non-hex input.
    /// </summary>
    public static bool TryParse(string? text, out RgbaColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length != 6 && span.Length != 8)
            return false;

        if (!TryHex(span.Slice(0, 2), out var r) ||
            !TryHex(span.Slice(2, 2), out var g) ||
            !TryHex(span.Slice(4, 2), out var b))
            return false;

        var a = OpaqueAlpha;
        if (span.Length == 8 && !TryHex(span.Slice(6, 2), out a))
            return false;

        color = new RgbaColor(r, g, b, a);
        return true;
    }

    private static bool TryHex(ReadOnlySpan<char> pair, out byte value)
        => byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
