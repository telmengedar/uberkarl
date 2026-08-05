using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Covers <see cref="BehaviorBindingResolver"/> -- the Definition → Resolved step (design #7704 C-2) applied
/// to <see cref="BehaviorBinding"/>, which <c>Uberkarl.Content.LevelLoader</c> calls exactly like it resolves
/// every other package reference (DiVoid #7738).
/// </summary>
[TestFixture]
public sealed class BehaviorBindingResolverTests
{
    [Test]
    public void Resolve_ScriptBinding_DecodesSourceAsUtf8()
    {
        var reference = ResourceReference.ToSelf(ResourcePath.Create("scripts/spike.poo"));
        var resolver = new FakeResolver(reference, "$onContact = $other => { player.hurt(10); }");

        var resolved = BehaviorBindingResolver.Resolve(resolver, BehaviorBinding.FromScript(reference));

        Assert.That(resolved.IsScript, Is.True);
        Assert.That(resolved.IsPredefined, Is.False);
        Assert.That(resolved.Script, Is.EqualTo("$onContact = $other => { player.hurt(10); }"));
    }

    [Test]
    public void Resolve_PredefinedBinding_PassesIdAndParametersThroughUnchanged()
    {
        var resolver = new FakeResolver(default, string.Empty); // never consulted for a predefined binding
        var parameters = new Dictionary<string, object?> { ["amount"] = 25 };

        var resolved = BehaviorBindingResolver.Resolve(resolver, BehaviorBinding.FromPredefined("healOnEnter", parameters));

        Assert.That(resolved.IsPredefined, Is.True);
        Assert.That(resolved.IsScript, Is.False);
        Assert.That(resolved.PredefinedId, Is.EqualTo("healOnEnter"));
        Assert.That(resolved.Parameters["amount"], Is.EqualTo(25));
    }

    [Test]
    public void Resolve_WhenScriptResourceMissing_PropagatesResolverException()
    {
        var reference = ResourceReference.ToSelf(ResourcePath.Create("scripts/missing.poo"));
        var resolver = new FakeResolver(default, string.Empty); // no entry for `reference` at all

        Assert.Throws<ResourceNotFoundException>(() => BehaviorBindingResolver.Resolve(resolver, BehaviorBinding.FromScript(reference)));
    }

    private sealed class FakeResolver : IResourceResolver
    {
        private readonly ResourceReference reference;
        private readonly byte[] payload;
        private readonly bool hasEntry;

        public FakeResolver(ResourceReference reference, string text)
        {
            this.reference = reference;
            payload = System.Text.Encoding.UTF8.GetBytes(text);
            hasEntry = !reference.Equals(default(ResourceReference));
        }

        public byte[] Resolve(ResourceReference candidate)
        {
            if (hasEntry && candidate.Equals(reference))
                return payload;
            throw new ResourceNotFoundException(candidate.Path);
        }

        public bool TryResolve(ResourceReference candidate, out byte[] resolvedPayload)
        {
            if (hasEntry && candidate.Equals(reference))
            {
                resolvedPayload = payload;
                return true;
            }
            resolvedPayload = System.Array.Empty<byte>();
            return false;
        }
    }
}
