using System;

namespace Uberkarl {

    /// <summary>
    /// Pure health-state transition math for <see cref="Player"/>: clamping, i-frame gating, and death
    /// edge-detection.
    /// </summary>
    public static class PlayerHealth {

        /// <summary>Invulnerability window granted after a hit lands, in seconds.</summary>
        public const double InvulnerabilityDurationSeconds = 1.0;

        /// <summary>Result of applying a hurt against the current health/i-frame state.</summary>
        public readonly record struct HurtResult(double Health, double InvulnerabilityRemaining, bool Applied, bool Died);

        /// <summary>Applies a hurt of <paramref name="amount"/> against <paramref name="health"/>, honoring <paramref name="invulnerabilityRemaining"/>.</summary>
        public static HurtResult Hurt(double health, double invulnerabilityRemaining, double amount) {
            if (invulnerabilityRemaining > 0)
                return new HurtResult(health, invulnerabilityRemaining, false, false);

            double next = Math.Max(0, health - amount);
            bool died = next <= 0 && health > 0;
            double nextInvulnerability = died ? 0 : InvulnerabilityDurationSeconds;
            return new HurtResult(next, nextInvulnerability, true, died);
        }

        /// <summary>Applies a heal of <paramref name="amount"/> against <paramref name="health"/>, clamped to <paramref name="maxHealth"/>.</summary>
        public static double Heal(double health, double maxHealth, double amount) => Math.Min(maxHealth, health + amount);

        /// <summary>Advances the invulnerability countdown by <paramref name="delta"/> seconds, floored at zero.</summary>
        public static double TickInvulnerability(double invulnerabilityRemaining, double delta) =>
            Math.Max(0, invulnerabilityRemaining - delta);
    }
}
