using System.IO;
using System.Linq;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Loads the real <c>content/sample.pkg</c> bytes from disk through the same calls
/// <c>LevelEditor.LoadFromResPath</c>/<c>StartPlaytest</c> make in production and asserts the spike tile
/// at cell (20,11) survives into <see cref="ResolvedLevel.EffectiveTileBehaviors"/>.
/// </summary>
[TestFixture]
public sealed class RealSamplePackagePlaytestTests
{
    private const int SpikeCellX = 20;
    private const int SpikeCellY = 11;

    [Test]
    public void RealSamplePackage_EditorPlaytestProjection_RegistersSpikeHurtOnContactAtItsAuthoredCell()
    {
        byte[] packageBytes = File.ReadAllBytes(FindSamplePackagePath());

        EditableLevel level = EditableLevelReader.FromPackageBytes(packageBytes);
        ResolvedLevel projection = EditableLevelSnapshot.ToResolvedLevel(level);

        var scriptedTiles = projection.EffectiveTileBehaviors().ToList();

        Assert.That(scriptedTiles, Is.Not.Empty,
            "the real content/sample.pkg produced ZERO scripted tile cells through the editor playtest projection " +
            "-- this is the exact symptom of DiVoid #7747 (HUD shows, spike does nothing), reproduced against the " +
            "actual shipped content rather than a synthetic in-memory package.");

        var spike = scriptedTiles.Where(t => t.Cell.X == SpikeCellX && t.Cell.Y == SpikeCellY).ToList();

        Assert.That(spike, Has.Count.EqualTo(1),
            $"expected exactly one scripted-tile registration at the spike's authored cell ({SpikeCellX},{SpikeCellY}) " +
            $"(tools/SampleContent/Program.cs 'Set(cells, {SpikeCellX}, {SpikeCellY}, SpikeTileId)'); found {spike.Count}. " +
            "If this is 0, the real package's tileset default binding is not reaching the editor playtest projection " +
            "even though the synthetic-package regression test passes -- a real-content-only divergence.");

        ResolvedBehaviorBinding binding = spike[0].Binding;
        Assert.Multiple(() =>
        {
            Assert.That(binding.IsPredefined, Is.True, "the spike's real binding is not a predefined binding.");
            Assert.That(binding.PredefinedId, Is.EqualTo(PredefinedBehaviors.HurtOnContact));
            Assert.That(binding.Parameters, Contains.Key("amount"));
        });
    }

    private static string FindSamplePackagePath()
    {
        DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "content", "sample.pkg");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate content/sample.pkg by walking up from the test directory " +
            $"'{TestContext.CurrentContext.TestDirectory}' -- repo layout changed?");
    }
}
