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

    /// <summary>
    /// Object predefined (DiVoid #7863, design #7704 §9.4 — "patrol predefined"): a <c>solid</c> object
    /// oscillates horizontally by <c>range</c> pixels (default 48) at <c>speed</c> px/s (default 24) — a
    /// moving platform. Bounded by construction (a ping-pong around its spawn position, no unbounded loop).
    /// </summary>
    public const string Patrol = "patrol";

    /// <summary>
    /// Object predefined (DiVoid #7863, design #7704 §9.4 — "rise-then-fall predefined"): a <c>passthrough</c>
    /// object bumps up then back down over a fixed frame count when hit from below (the player moving
    /// upward on contact) — a jump-block/question-block. Ignores contact while already bumping or not from
    /// below, so a single hit produces exactly one bounded reaction.
    /// </summary>
    public const string BumpOnHitFromBelow = "bumpOnHitFromBelow";

    private const string AmountParameter = "amount";
    private const string SpeedParameter = "speed";
    private const string RangeParameter = "range";
    private const string RiseParameter = "rise";
    private const double HurtOnContactDefaultAmount = 10;
    private const double HealOnEnterDefaultAmount = 20;
    private const double PatrolDefaultSpeed = 24;
    private const double PatrolDefaultRange = 48;
    private const double BumpDefaultRise = 6;

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

            case Patrol:
                source = $$"""
                    $onSpawn = [] => {
                        self.setState("dir", 1);
                        self.setState("origin", self.position.x);
                    }
                    $onUpdate = $delta => {
                        $dir = self.getState("dir");
                        self.moveBy({{FormatParameter(parameters, SpeedParameter, PatrolDefaultSpeed)}} * delta * dir, 0);
                        $traveled = self.position.x - self.getState("origin");
                        if (traveled > {{FormatParameter(parameters, RangeParameter, PatrolDefaultRange)}}) { self.setState("dir", -1); }
                        if (traveled < 0) { self.setState("dir", 1); }
                    }
                    { "onSpawn": onSpawn, "onUpdate": onUpdate }
                    """;
                return true;

            case BumpOnHitFromBelow:
                source = $$"""
                    $onContact = $other => {
                        if (self.getState("bumping") != true) {
                            if (player.velocity.y < 0) {
                                self.setState("bumping", true);
                                self.setState("bumpFrames", 12);
                            }
                        }
                    }
                    $onUpdate = $delta => {
                        if (self.getState("bumping") == true) {
                            $frames = self.getState("bumpFrames");
                            if (frames > 6) { self.moveBy(0, -{{FormatParameter(parameters, RiseParameter, BumpDefaultRise)}}); }
                            if (frames <= 6) { self.moveBy(0, {{FormatParameter(parameters, RiseParameter, BumpDefaultRise)}}); }
                            self.setState("bumpFrames", frames - 1);
                            if (frames <= 1) { self.setState("bumping", false); }
                        }
                    }
                    { "onContact": onContact, "onUpdate": onUpdate }
                    """;
                return true;

            default:
                source = string.Empty;
                return false;
        }
    }

    private static string FormatAmount(IReadOnlyDictionary<string, object?> parameters, double fallback)
        => FormatParameter(parameters, AmountParameter, fallback);

    /// <summary>
    /// Renders one numeric template parameter. A value that cannot be read as a number is bad package data
    /// rather than a bug, so the failure is rethrown naming the key and the offending value: the loader turns
    /// it into a quarantine, which is what keeps a malformed package from throwing during level load (#8237).
    /// </summary>
    private static string FormatParameter(IReadOnlyDictionary<string, object?> parameters, string key, double fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
            return fallback.ToString(CultureInfo.InvariantCulture);

        try {
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) {
            throw new FormatException($"parameter '{key}' must be a number, but was '{raw}'", ex);
        }
    }
}
