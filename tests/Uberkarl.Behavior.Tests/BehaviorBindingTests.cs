using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Behavior.Tests;

/// <summary>Covers <see cref="BehaviorBinding"/> (design #7704 §5.2/§6) — the shared script-ref-OR-predefined value every scriptable subject uses.</summary>
[TestFixture]
public sealed class BehaviorBindingTests
{
    private static readonly ResourceReference SampleScript = ResourceReference.ToSelf(ResourcePath.Create("scripts/spike.poo"));

    [Test]
    public void FromScript_SetsScript_NotPredefined()
    {
        var binding = BehaviorBinding.FromScript(SampleScript);

        Assert.That(binding.IsScript, Is.True);
        Assert.That(binding.IsPredefined, Is.False);
        Assert.That(binding.Script, Is.EqualTo(SampleScript));
        Assert.That(binding.PredefinedId, Is.Null);
        Assert.That(binding.Parameters, Is.Empty);
    }

    [Test]
    public void FromPredefined_SetsPredefined_NotScript()
    {
        var binding = BehaviorBinding.FromPredefined("patrol", new Dictionary<string, object?> { ["speed"] = 2.0 });

        Assert.That(binding.IsPredefined, Is.True);
        Assert.That(binding.IsScript, Is.False);
        Assert.That(binding.Script, Is.Null);
        Assert.That(binding.PredefinedId, Is.EqualTo("patrol"));
        Assert.That(binding.Parameters["speed"], Is.EqualTo(2.0));
    }

    [Test]
    public void FromPredefined_WithoutParameters_HasEmptyParameters()
    {
        var binding = BehaviorBinding.FromPredefined("patrol");

        Assert.That(binding.Parameters, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void FromPredefined_RejectsBlankId(string id)
    {
        Assert.Throws<ArgumentException>(() => BehaviorBinding.FromPredefined(id));
    }
}
