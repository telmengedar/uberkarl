namespace Uberkarl.Behavior;

using System.Globalization;

/// <summary>
/// The curated, first-party predefined behavior library (design #7704 §5.7/Phase-1 seed — "a small predefined
/// library seed"). Each entry is a stable id + a Pooscript source **template**, audited to be bounded (no
/// unbounded loops) so the cooperative-plus-watchdog safety story already suffices for it today, per design
/// §8.4/D-5's curated-first interim. Authors reference a predefined behavior by id + parameters
/// (<see cref="BehaviorBinding.FromPredefined"/>); only numeric parameters are supported in P1 — every
/// template substitutes its parameter as a plain numeric literal, so there is no free-text injection surface
/// even though the substitution itself is simple string formatting (the templates are engine-authored, not
/// script-author-authored — the parameter is the only author-controlled value, and it can only ever become a
/// number here).
/// </summary>
public static class PredefinedBehaviors
{
    /// <summary>Tile predefined (design task #7738 — "a hurt-on-contact spike tile"): on contact, damages the player by <c>amount</c> (default 10).</summary>
    public const string HurtOnContact = "hurtOnContact";

    /// <summary>Area-trigger predefined (design task #7738 — "an on-enter trigger"): on enter, heals the player by <c>amount</c> (default 20) — a distinct intent (heal vs. hurt) proving the same binding/dispatch path serves more than one demo.</summary>
    public const string HealOnEnter = "healOnEnter";

    private const string AmountParameter = "amount";
    private const double HurtOnContactDefaultAmount = 10;
    private const double HealOnEnterDefaultAmount = 20;

    /// <summary>
    /// Resolves a predefined id + parameters into ready-to-compile Pooscript source. False for an unknown id
    /// (the caller quarantines rather than throwing — matches <see cref="BehaviorLoader.Compile"/>'s "always
    /// return a usable, possibly-already-quarantined result" contract).
    /// </summary>
    public static bool TryGetSource(string predefinedId, IReadOnlyDictionary<string, object?> parameters, out string source)
    {
        switch (predefinedId)
        {
            case HurtOnContact:
                source = $$"""
                    $onContact = $other => { player.hurt({{FormatAmount(parameters, HurtOnContactDefaultAmount)}}); }
                    { "onContact": onContact }
                    """;
                return true;

            case HealOnEnter:
                source = $$"""
                    $onEnter = $who => { player.heal({{FormatAmount(parameters, HealOnEnterDefaultAmount)}}); }
                    { "onEnter": onEnter }
                    """;
                return true;

            default:
                source = string.Empty;
                return false;
        }
    }

    private static string FormatAmount(IReadOnlyDictionary<string, object?> parameters, double fallback)
    {
        var amount = parameters.TryGetValue(AmountParameter, out var value) && value is not null
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : fallback;
        return amount.ToString(CultureInfo.InvariantCulture);
    }
}
