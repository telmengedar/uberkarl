using NUnit.Framework;
using Uberkarl.Editor.Input;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers the engine-agnostic pop-in menu lifecycle: press-vs-hold edge detection, trigger arbitration, close arbitration, session sequencing, and latch-direction reduction.</summary>
[TestFixture]
public sealed class MenuSessionTests
{
    private const float HoldThreshold = 0.2f;

    [Test]
    public void HoldWatch_HeldPastThreshold_CrossesHoldExactlyOnce()
    {
        HoldWatch watch = new(HoldThreshold);

        watch.Update(pressed: true, delta: 0.1f);
        Assert.That(watch.JustCrossedHold, Is.False);

        watch.Update(pressed: true, delta: 0.25f);
        Assert.That(watch.JustCrossedHold, Is.True);

        watch.Update(pressed: true, delta: 0.1f);
        Assert.That(watch.JustCrossedHold, Is.False);
    }

    [Test]
    public void HoldWatch_ReleaseAfterHold_IsJustReleased_NotATap()
    {
        HoldWatch watch = new(HoldThreshold);
        watch.Update(pressed: true, delta: 0.1f);
        watch.Update(pressed: true, delta: 0.25f);

        watch.Update(pressed: false, delta: 0.1f);

        Assert.Multiple(() =>
        {
            Assert.That(watch.JustReleased, Is.True);
            Assert.That(watch.ReleasedAsTap, Is.False);
        });
    }

    [Test]
    public void HoldWatch_ReleaseBeforeThreshold_IsReleasedAsTap()
    {
        HoldWatch watch = new(HoldThreshold);
        watch.Update(pressed: true, delta: 0.05f);

        watch.Update(pressed: false, delta: 0.01f);

        Assert.Multiple(() =>
        {
            Assert.That(watch.JustReleased, Is.True);
            Assert.That(watch.ReleasedAsTap, Is.True);
        });
    }

    [Test]
    public void HoldWatch_ReleaseFlags_AreOneFramePulses()
    {
        HoldWatch watch = new(HoldThreshold);
        watch.Update(pressed: true, delta: 0.05f);
        watch.Update(pressed: false, delta: 0.01f);

        watch.Update(pressed: false, delta: 0.01f);

        Assert.Multiple(() =>
        {
            Assert.That(watch.JustReleased, Is.False);
            Assert.That(watch.ReleasedAsTap, Is.False);
        });
    }

    [Test]
    [Description("HeldTime must reset to 0 on every press-start frame, not just the first — otherwise a second press inherits the first press's HeldTime and JustCrossedHold never fires again.")]
    public void HoldWatch_SecondPress_CrossesHoldAgain_AfterAnEarlierPressAlreadyCrossed()
    {
        HoldWatch watch = new(HoldThreshold);
        watch.Update(pressed: true, delta: 0.05f);
        watch.Update(pressed: true, delta: 0.3f);
        Assert.That(watch.JustCrossedHold, Is.True, "sanity: first press crosses hold.");
        watch.Update(pressed: false, delta: 0.05f);
        watch.Update(pressed: true, delta: 0.05f);

        watch.Update(pressed: true, delta: 0.3f);

        Assert.That(watch.JustCrossedHold, Is.True);
    }

    [Test]
    [Description("HeldTime landing exactly on the threshold must cross it, not require strictly more than it.")]
    public void HoldWatch_HeldExactlyAtThreshold_CrossesHold()
    {
        HoldWatch watch = new(HoldThreshold);
        watch.Update(pressed: true, delta: 0.05f);

        watch.Update(pressed: true, delta: HoldThreshold);

        Assert.That(watch.JustCrossedHold, Is.True);
    }

    [Test]
    public void MenuSession_OpenRequested_OpensAndTransitionsToTransient()
    {
        MenuSession session = new();

        MenuSessionTransition opened = session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);

        Assert.Multiple(() =>
        {
            Assert.That(opened.Effect, Is.EqualTo(MenuSessionEffect.Open));
            Assert.That(opened.State, Is.EqualTo(MenuSessionState.Transient));
            Assert.That(session.State, Is.EqualTo(MenuSessionState.Transient));
        });
    }

    [Test]
    public void MenuSession_OpenRequested_WhileAlreadyOpen_DoesNotReopen()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);

        MenuSessionTransition second = session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);

        Assert.Multiple(() =>
        {
            Assert.That(second.Effect, Is.EqualTo(MenuSessionEffect.None));
            Assert.That(second.State, Is.EqualTo(MenuSessionState.Transient));
        });
    }

    [Test]
    public void MenuSession_ReleaseNotATap_Resolves_AndCloses()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);

        MenuSessionTransition resolved = session.Step(openRequested: false, triggerReleased: true, releasedAsTap: false);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Effect, Is.EqualTo(MenuSessionEffect.Close));
            Assert.That(resolved.State, Is.EqualTo(MenuSessionState.Closed));
        });
    }

    [Test]
    public void MenuSession_AfterResolving_TheSameReleaseDoesNotResolveAgain()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        session.Step(openRequested: false, triggerReleased: true, releasedAsTap: false);

        MenuSessionTransition repeated = session.Step(openRequested: false, triggerReleased: true, releasedAsTap: false);

        Assert.That(repeated.Effect, Is.EqualTo(MenuSessionEffect.None));
    }

    [Test]
    public void MenuSession_ReleaseAsTap_Latches_AndStaysOpen()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);

        MenuSessionTransition latched = session.Step(openRequested: false, triggerReleased: true, releasedAsTap: true);

        Assert.Multiple(() =>
        {
            Assert.That(latched.Effect, Is.EqualTo(MenuSessionEffect.Latch));
            Assert.That(latched.State, Is.EqualTo(MenuSessionState.Latched));
        });
    }

    [Test]
    public void MenuSession_OnceLatched_FurtherTriggerReleasesHaveNoEffect()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        session.Step(openRequested: false, triggerReleased: true, releasedAsTap: true);

        MenuSessionTransition stray = session.Step(openRequested: false, triggerReleased: true, releasedAsTap: false);

        Assert.Multiple(() =>
        {
            Assert.That(stray.Effect, Is.EqualTo(MenuSessionEffect.None));
            Assert.That(stray.State, Is.EqualTo(MenuSessionState.Latched));
        });
    }

    [Test]
    public void MenuSession_OpenRequested_WhileLatched_DoesNotOpenAnother()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        session.Step(openRequested: false, triggerReleased: true, releasedAsTap: true);

        MenuSessionTransition secondTrigger = session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);

        Assert.That(secondTrigger.Effect, Is.EqualTo(MenuSessionEffect.None));
        Assert.That(secondTrigger.State, Is.EqualTo(MenuSessionState.Latched));
    }

    [Test]
    public void MenuSession_Reset_FromLatched_ReturnsToClosed()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        session.Step(openRequested: false, triggerReleased: true, releasedAsTap: true);

        session.Reset();

        Assert.That(session.State, Is.EqualTo(MenuSessionState.Closed));
    }

    [Test]
    public void MenuSession_Reset_IsIdempotent_WhenAlreadyClosed()
    {
        MenuSession session = new();

        session.Reset();

        Assert.That(session.State, Is.EqualTo(MenuSessionState.Closed));
    }

    [Test]
    public void MenuSession_Resolve_WhileLatched_ClosesTheSession()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        session.Step(openRequested: false, triggerReleased: true, releasedAsTap: true);

        MenuSessionTransition resolved = session.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Effect, Is.EqualTo(MenuSessionEffect.Close));
            Assert.That(resolved.State, Is.EqualTo(MenuSessionState.Closed));
            Assert.That(session.State, Is.EqualTo(MenuSessionState.Closed));
        });
    }

    [Test]
    public void MenuSession_Resolve_WhileTransient_ClosesTheSession()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);

        MenuSessionTransition resolved = session.Resolve();

        Assert.That(resolved.Effect, Is.EqualTo(MenuSessionEffect.Close));
    }

    [Test]
    public void MenuSession_Resolve_WhileClosed_IsANoOp()
    {
        MenuSession session = new();

        MenuSessionTransition resolved = session.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Effect, Is.EqualTo(MenuSessionEffect.None));
            Assert.That(resolved.State, Is.EqualTo(MenuSessionState.Closed));
        });
    }

    [Test]
    [Description("Pins the fifth #8525 §9 property for the latched path: every terminal transition produces exactly one Close effect. A stray second Resolve() after the session already closed must not produce a second Close — this is the invariant that lets the glue call EndMenu() from a single site without a discipline-only guarantee.")]
    public void MenuSession_Resolve_AfterResolving_TheSameResolveDoesNotCloseAgain()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        session.Step(openRequested: false, triggerReleased: true, releasedAsTap: true);
        session.Resolve();

        MenuSessionTransition repeated = session.Resolve();

        Assert.That(repeated.Effect, Is.EqualTo(MenuSessionEffect.None));
    }

    [Test]
    [Description("The two ways to reach Close — a trigger release (Step) and a surface-driven interaction (Resolve) — must agree on a single terminal transition: once Step has already closed the session, a same-frame Resolve must not close it a second time.")]
    public void MenuSession_Resolve_AfterStepAlreadyClosed_DoesNotCloseAgain()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        session.Step(openRequested: false, triggerReleased: true, releasedAsTap: false);

        MenuSessionTransition resolved = session.Resolve();

        Assert.That(resolved.Effect, Is.EqualTo(MenuSessionEffect.None));
    }

    [Test]
    [Description("DiVoid #8590 W-2: a same-frame cancel arriving on a Step-driven close — the gamepad/keyboard hold-release path — must still force-cancel. Pre-fix, ForceCancel was only assigned inside the Effect == None branch, so a cancel landing on the same frame Step() itself already returned Close was silently dropped and the aimed wedge got committed instead. This is exactly the path a Resolve(cancel:true) -> Cancel test cannot reach, since Step already produced Close before Resolve() is ever considered.")]
    public void MenuCloseArbitration_CancelRequested_OnAStepDrivenClose_StillForcesCancel()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        MenuSessionTransition stepClosed = session.Step(openRequested: false, triggerReleased: true, releasedAsTap: false);

        MenuCloseArbitration.Resolution resolution =
            MenuCloseArbitration.Resolve(session, stepClosed, cancelRequested: true, resolveRequested: false);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Transition.Effect, Is.EqualTo(MenuSessionEffect.Close));
            Assert.That(resolution.ForceCancel, Is.True);
        });
    }

    [Test]
    public void LatchDirection_Left_IsNegative()
    {
        (bool negative, bool positive) = LatchDirection.Reduce(left: true, right: false, up: false, down: false);
        Assert.That((negative, positive), Is.EqualTo((true, false)));
    }

    [Test]
    public void LatchDirection_Up_IsNegative()
    {
        (bool negative, bool positive) = LatchDirection.Reduce(left: false, right: false, up: true, down: false);
        Assert.That((negative, positive), Is.EqualTo((true, false)));
    }

    [Test]
    public void LatchDirection_Right_IsPositive()
    {
        (bool negative, bool positive) = LatchDirection.Reduce(left: false, right: true, up: false, down: false);
        Assert.That((negative, positive), Is.EqualTo((false, true)));
    }

    [Test]
    public void LatchDirection_Down_IsPositive()
    {
        (bool negative, bool positive) = LatchDirection.Reduce(left: false, right: false, up: false, down: true);
        Assert.That((negative, positive), Is.EqualTo((false, true)));
    }

    [Test]
    public void LatchDirection_NoInput_IsNeitherNegativeNorPositive()
    {
        (bool negative, bool positive) = LatchDirection.Reduce(left: false, right: false, up: false, down: false);
        Assert.That((negative, positive), Is.EqualTo((false, false)));
    }

    [Test]
    public void MenuTriggerArbitration_HoldCrossing_WinsOverTap()
    {
        MenuSession session = new();
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: false, releasedAsTap: true),
            new(justCrossedHold: true, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
        };

        MenuTriggerArbitration.Attempt attempt = MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: true, session);

        Assert.Multiple(() =>
        {
            Assert.That(attempt.TriggerIndex, Is.EqualTo(1));
            Assert.That(attempt.LatchedImmediately, Is.False);
            Assert.That(session.State, Is.EqualTo(MenuSessionState.Transient));
        });
    }

    [Test]
    [Description("DiVoid #8525 §11 U3: a trigger targeting a list surface (Tiles) has no aim to track, so even a genuine hold-crossing must latch immediately rather than enter Transient — unlike a radial-targeting trigger, which the case above pins as staying Transient on the same reading.")]
    public void MenuTriggerArbitration_HoldCrossing_OnAnAutoLatchIndex_LatchesImmediately()
    {
        MenuSession session = new();
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: true, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
        };

        MenuTriggerArbitration.Attempt attempt =
            MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: index => index == 0, hasSession: true, session);

        Assert.Multiple(() =>
        {
            Assert.That(attempt.TriggerIndex, Is.EqualTo(0));
            Assert.That(attempt.LatchedImmediately, Is.True);
            Assert.That(session.State, Is.EqualTo(MenuSessionState.Latched));
        });
    }

    [Test]
    public void MenuTriggerArbitration_MultipleHoldCrossings_PicksTheFirstByPriority()
    {
        MenuSession session = new();
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: true, releasedAsTap: false),
            new(justCrossedHold: true, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
        };

        MenuTriggerArbitration.Attempt attempt = MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: true, session);

        Assert.That(attempt.TriggerIndex, Is.EqualTo(0));
    }

    [Test]
    public void MenuTriggerArbitration_Tap_OpensAndLatchesImmediately()
    {
        MenuSession session = new();
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: true),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
        };

        MenuTriggerArbitration.Attempt attempt = MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: true, session);

        Assert.Multiple(() =>
        {
            Assert.That(attempt.TriggerIndex, Is.EqualTo(1));
            Assert.That(attempt.LatchedImmediately, Is.True);
            Assert.That(session.State, Is.EqualTo(MenuSessionState.Latched));
        });
    }

    [Test]
    public void MenuTriggerArbitration_ExcludedIndexTap_DoesNotOpen()
    {
        MenuSession session = new();
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: true),
        };

        MenuTriggerArbitration.Attempt attempt = MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: true, session);

        Assert.That(attempt.Opened, Is.False);
    }

    [Test]
    public void MenuTriggerArbitration_NothingCrossedOrTapped_DoesNotOpen()
    {
        MenuSession session = new();
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
        };

        MenuTriggerArbitration.Attempt attempt = MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: true, session);

        Assert.That(attempt.Opened, Is.False);
    }

    [Test]
    [Description("DiVoid #8545 CF-1: the prior soft-lock came from stepping the session before checking whether opening was actually possible, leaving the session stuck Transient forever with nothing to reach it. A blocked open must leave the session fully untouched.")]
    public void MenuTriggerArbitration_WhenCannotOpen_LeavesTheSessionUntouched_SoALaterOpenStillWorks()
    {
        MenuSession session = new();
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: true, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
        };

        MenuTriggerArbitration.Attempt blocked = MenuTriggerArbitration.TryOpen(_ => false, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: true, session);

        Assert.Multiple(() =>
        {
            Assert.That(blocked.Opened, Is.False);
            Assert.That(session.State, Is.EqualTo(MenuSessionState.Closed));
        });

        MenuTriggerArbitration.Attempt allowed = MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: true, session);

        Assert.That(allowed.Opened, Is.True);
    }

    [Test]
    public void MenuTriggerArbitration_WhenSessionAlreadyOpen_DoesNotOpenAgain()
    {
        MenuSession session = new();
        session.Step(openRequested: true, triggerReleased: false, releasedAsTap: false);
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: true, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
        };

        MenuTriggerArbitration.Attempt attempt = MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: true, session);

        Assert.That(attempt.Opened, Is.False);
    }

    [Test]
    [Description("DiVoid #8578 W-6: LevelEditor.CanOpenTrigger's `session != null` conjunct was CF-1's own precondition and had no test reaching it — deleting it left every one of the 443 tests green. Session-liveness is now an explicit `hasSession` input to arbitration, evaluated before canOpen, so a null session can never open a menu.")]
    public void MenuTriggerArbitration_WhenNoSession_DoesNotOpen()
    {
        MenuSession session = new();
        MenuTriggerArbitration.Reading[] readings = {
            new(justCrossedHold: true, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
            new(justCrossedHold: false, releasedAsTap: false),
        };

        MenuTriggerArbitration.Attempt attempt = MenuTriggerArbitration.TryOpen(_ => true, readings, excludeTapIndex: 3, autoLatch: _ => false, hasSession: false, session);

        Assert.Multiple(() =>
        {
            Assert.That(attempt.Opened, Is.False);
            Assert.That(session.State, Is.EqualTo(MenuSessionState.Closed));
        });
    }
}
