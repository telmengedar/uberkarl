using System;

namespace Uberkarl {

    /// <summary>
    /// Pure health-state transition math for <see cref="Player"/> (DiVoid #7743 — making the P1 hurt/heal
    /// intent plumbing visible/playable). Deliberately has no Godot dependency, so it is unit-testable
    /// directly from <c>Uberkarl.Editor.Tests</c> without a running engine/scene tree — the same pattern
    /// <see cref="TileMapLevelBuilder.ScrollScaleFor"/> already established for pinning pure math that lives
    /// inside otherwise Godot-coupled game-side classes. <see cref="Player"/> is the sole caller; this type
    /// only exists to keep the intent-&gt;health arithmetic (clamping, i-frame gating, death edge-detection)
    /// verifiable outside the engine.
    /// </summary>
    public static class PlayerHealth {

        /// <summary>Invulnerability window granted after a hit lands (design #7743) — long enough that
        /// resting against a continuously-touching hazard (e.g. a spike tile, edge-triggered per contact
        /// state change) can't chain-drain health frame over frame.</summary>
        public const double InvulnerabilityDurationSeconds = 1.0;

        /// <summary>Result of applying a <see cref="Player.Hurt"/> call against the current health/i-frame
        /// state. <see cref="Applied"/> is false when the hit was absorbed by an active invulnerability
        /// window (health/i-frame timer both left untouched). <see cref="Died"/> is true only on the frame
        /// health crosses from positive into zero (never re-fires while already dead).</summary>
        public readonly record struct HurtResult(double Health, double InvulnerabilityRemaining, bool Applied, bool Died);

        /// <summary>Applies a hurt of <paramref name="amount"/> against <paramref name="health"/>, honoring
        /// <paramref name="invulnerabilityRemaining"/> (design #7743 i-frames — heal has no such gate, only
        /// hurt). Health is clamped to zero; a fresh invulnerability window starts whenever damage is
        /// actually applied and the player survives.</summary>
        public static HurtResult Hurt(double health, double invulnerabilityRemaining, double amount) {
            if (invulnerabilityRemaining > 0)
                return new HurtResult(health, invulnerabilityRemaining, false, false);

            double next = Math.Max(0, health - amount);
            bool died = next <= 0 && health > 0;
            double nextInvulnerability = died ? 0 : InvulnerabilityDurationSeconds;
            return new HurtResult(next, nextInvulnerability, true, died);
        }

        /// <summary>Applies a heal of <paramref name="amount"/> against <paramref name="health"/>, clamped to
        /// <paramref name="maxHealth"/>. No invulnerability interaction — healing is never gated.</summary>
        public static double Heal(double health, double maxHealth, double amount) => Math.Min(maxHealth, health + amount);

        /// <summary>Advances the invulnerability countdown by <paramref name="delta"/> seconds, floored at
        /// zero.</summary>
        public static double TickInvulnerability(double invulnerabilityRemaining, double delta) =>
            Math.Max(0, invulnerabilityRemaining - delta);
    }
}
