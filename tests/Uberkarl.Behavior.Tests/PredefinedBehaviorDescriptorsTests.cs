using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Covers the M4 predefined-descriptor table (design #8049 §6.2/§7, #8525 §12): the applicability filter's
/// per-kind boundaries, the descriptor data itself (id/label/parameters as literal values), and the
/// gamepad stepper arithmetic S3's playtest walk depends on (24 -> 40 over four presses).
/// </summary>
[TestFixture]
public sealed class PredefinedBehaviorDescriptorsTests
{
    [Test]
    public void ApplicableTo_Tile_ReturnsExactlyHurtOnContact()
    {
        var ids = PredefinedBehaviors.ApplicableTo(BehaviorSubjectKind.Tile).Select(d => d.Id).ToArray();
        Assert.That(ids, Is.EqualTo(new[] { "hurtOnContact" }));
    }

    [Test]
    public void ApplicableTo_Trigger_ReturnsExactlyHealOnEnter()
    {
        var ids = PredefinedBehaviors.ApplicableTo(BehaviorSubjectKind.Trigger).Select(d => d.Id).ToArray();
        Assert.That(ids, Is.EqualTo(new[] { "healOnEnter" }));
    }

    [Test]
    public void ApplicableTo_Object_ReturnsHurtOnContact_Patrol_AndBump_InDescriptorOrder()
    {
        var ids = PredefinedBehaviors.ApplicableTo(BehaviorSubjectKind.Object).Select(d => d.Id).ToArray();
        Assert.That(ids, Is.EqualTo(new[] { "hurtOnContact", "patrol", "bumpOnHitFromBelow" }));
    }

    [Test]
    [Description("The runtime never dispatches an onLevelStart/onUpdate-only event any of today's four predefineds handles to the level script, so an empty list here is correct, not a bug.")]
    public void ApplicableTo_LevelScript_ReturnsNoPredefineds()
    {
        var descriptors = PredefinedBehaviors.ApplicableTo(BehaviorSubjectKind.LevelScript).ToArray();
        Assert.That(descriptors, Is.Empty);
    }

    [Test]
    public void HurtOnContact_Descriptor_HasLabelAndOneAmountParameter()
    {
        PredefinedBehaviorDescriptor descriptor = PredefinedBehaviors.Descriptors.First(d => d.Id == PredefinedBehaviors.HurtOnContact);

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Label, Is.EqualTo("Hurt on Contact"));
            Assert.That(descriptor.Parameters, Has.Count.EqualTo(1));
            Assert.That(descriptor.Parameters[0].Name, Is.EqualTo("amount"));
            Assert.That(descriptor.Parameters[0].Default, Is.EqualTo(10));
        });
    }

    [Test]
    public void Patrol_Descriptor_HasLabelAndTwoParameters_SpeedThenRange()
    {
        PredefinedBehaviorDescriptor descriptor = PredefinedBehaviors.Descriptors.First(d => d.Id == PredefinedBehaviors.Patrol);

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Label, Is.EqualTo("Patrol"));
            Assert.That(descriptor.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "speed", "range" }));
            Assert.That(descriptor.Parameters[0].Default, Is.EqualTo(24));
            Assert.That(descriptor.Parameters[1].Default, Is.EqualTo(48));
        });
    }

    [Test]
    [Description("Pins the S3 gamepad walk (assign patrol, change speed 24 -> 40) at the pure descriptor level: four +1 stepper presses land exactly on 40, no rounding.")]
    public void Patrol_SpeedParameter_FourStepsUp_ReachesForty()
    {
        PredefinedParameterDescriptor speed = PredefinedBehaviors.Descriptors
            .First(d => d.Id == PredefinedBehaviors.Patrol).Parameter("speed");

        double value = speed.Default;
        for (int i = 0; i < 4; i++)
            value = speed.Step(value, +1);

        Assert.That(value, Is.EqualTo(40));
    }

    [Test]
    public void AppliesTo_SyntheticDescriptor_BoundariesAcrossOneTwoThreeAndFourOfFourKinds()
    {
        PredefinedParameterDescriptor[] noParameters = Array.Empty<PredefinedParameterDescriptor>();

        PredefinedBehaviorDescriptor oneKind = new("t1", "One Kind", new[] { BehaviorSubjectKind.Tile }, noParameters);
        PredefinedBehaviorDescriptor threeKinds = new("t3", "Three Kinds",
            new[] { BehaviorSubjectKind.Tile, BehaviorSubjectKind.Trigger, BehaviorSubjectKind.Object }, noParameters);
        PredefinedBehaviorDescriptor fourKinds = new("t4", "Four Kinds",
            new[] { BehaviorSubjectKind.Tile, BehaviorSubjectKind.Trigger, BehaviorSubjectKind.Object, BehaviorSubjectKind.LevelScript }, noParameters);

        Assert.Multiple(() =>
        {
            Assert.That(oneKind.AppliesTo(BehaviorSubjectKind.Tile), Is.True);
            Assert.That(oneKind.AppliesTo(BehaviorSubjectKind.Object), Is.False);

            Assert.That(threeKinds.AppliesTo(BehaviorSubjectKind.Tile), Is.True);
            Assert.That(threeKinds.AppliesTo(BehaviorSubjectKind.Trigger), Is.True);
            Assert.That(threeKinds.AppliesTo(BehaviorSubjectKind.Object), Is.True);
            Assert.That(threeKinds.AppliesTo(BehaviorSubjectKind.LevelScript), Is.False,
                "the fourth kind is exactly where an off-by-one in a hand-rolled filter hides.");

            Assert.That(fourKinds.AppliesTo(BehaviorSubjectKind.LevelScript), Is.True);
        });
    }

    [Test]
    public void PredefinedParameterDescriptor_Step_ClampsAtMinAndMax()
    {
        PredefinedParameterDescriptor amount = new("amount", 10, min: 1, max: 100, increment: 5);

        Assert.Multiple(() =>
        {
            Assert.That(amount.Step(1, -1), Is.EqualTo(1), "stepping below Min clamps, does not go negative.");
            Assert.That(amount.Step(100, +1), Is.EqualTo(100), "stepping above Max clamps.");
            Assert.That(amount.Step(10, +1), Is.EqualTo(15));
        });
    }
}
