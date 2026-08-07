using NUnit.Framework;
using Uberkarl;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Pins the intent -&gt; health arithmetic that <see cref="Player.Hurt"/>/<see cref="Player.Heal"/> delegate to:
/// clamping, the invulnerability gate, the invulnerability countdown, and death edge-detection.
/// </summary>
[TestFixture]
public sealed class PlayerHealthTests {

    [Test]
    public void Hurt_ReducesHealthByAmount() {
        PlayerHealth.HurtResult result = PlayerHealth.Hurt(health: 100, invulnerabilityRemaining: 0, amount: 30);

        Assert.That(result.Health, Is.EqualTo(70));
        Assert.That(result.Applied, Is.True);
        Assert.That(result.Died, Is.False);
    }

    [Test]
    public void Hurt_ClampsAtZero_RatherThanGoingNegative() {
        PlayerHealth.HurtResult result = PlayerHealth.Hurt(health: 10, invulnerabilityRemaining: 0, amount: 999);

        Assert.That(result.Health, Is.EqualTo(0));
    }

    [Test]
    public void Hurt_StartsInvulnerabilityWindow_WhenDamageApplied() {
        PlayerHealth.HurtResult result = PlayerHealth.Hurt(health: 100, invulnerabilityRemaining: 0, amount: 10);

        Assert.That(result.InvulnerabilityRemaining, Is.EqualTo(PlayerHealth.InvulnerabilityDurationSeconds));
    }

    [Test]
    public void Hurt_IsNoOp_WhileInvulnerabilityWindowActive() {
        PlayerHealth.HurtResult result = PlayerHealth.Hurt(health: 100, invulnerabilityRemaining: 0.4, amount: 40);

        Assert.That(result.Applied, Is.False, "a hit landing during the i-frame window must not touch health");
        Assert.That(result.Health, Is.EqualTo(100));
        Assert.That(result.InvulnerabilityRemaining, Is.EqualTo(0.4), "the existing countdown must be left untouched, not restarted");
    }

    [Test]
    [Description("A second hurt landing while already at 0 health must not re-raise Died.")]
    public void Hurt_RaisesDied_OnlyOnTheFrameHealthFirstReachesZero() {
        PlayerHealth.HurtResult lethal = PlayerHealth.Hurt(health: 5, invulnerabilityRemaining: 0, amount: 999);
        Assert.That(lethal.Died, Is.True);

        PlayerHealth.HurtResult alreadyDead = PlayerHealth.Hurt(health: 0, invulnerabilityRemaining: 0, amount: 10);
        Assert.That(alreadyDead.Died, Is.False);
    }

    [Test]
    [Description("Respawn grants its own fresh invulnerability window; the death transition itself must not stack one.")]
    public void Hurt_OnDeath_LeavesNoInvulnerabilityWindow() {
        PlayerHealth.HurtResult result = PlayerHealth.Hurt(health: 5, invulnerabilityRemaining: 0, amount: 999);

        Assert.That(result.InvulnerabilityRemaining, Is.EqualTo(0));
    }

    [Test]
    public void Heal_IncreasesHealthByAmount() {
        double health = PlayerHealth.Heal(health: 50, maxHealth: 100, amount: 20);

        Assert.That(health, Is.EqualTo(70));
    }

    [Test]
    public void Heal_ClampsAtMaxHealth() {
        double health = PlayerHealth.Heal(health: 90, maxHealth: 100, amount: 50);

        Assert.That(health, Is.EqualTo(100));
    }

    [TestCase(1.0, 0.4, 0.6)]
    [TestCase(0.05, 0.2, 0)]
    public void TickInvulnerability_CountsDownAndFloorsAtZero(double remaining, double delta, double expected) {
        double next = PlayerHealth.TickInvulnerability(remaining, delta);

        Assert.That(next, Is.EqualTo(expected).Within(0.0001));
    }
}
