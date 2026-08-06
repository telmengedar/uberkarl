using System.IO;
using System.Linq;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// DiVoid #7747 (REOPENED): every regression test added for this bug so far (<see cref="EditableLevelSnapshotBehaviorTests"/>,
/// <see cref="PlaytestProjectionTests"/>) builds its OWN synthetic package in memory via <see cref="PackageBuilder"/> --
/// none of them ever load the actual <c>content/sample.pkg</c> binary checked into the repo, which is the file
/// <c>Uberkarl.Editor.LevelEditor._Ready</c> loads by default (<c>LoadFromResPath(SamplePackagePath)</c>) and therefore the
/// exact file Toni's live editor-Play run plays. A synthetic-package test proves the CODE PATH is correct; it cannot
/// prove the REAL FILE ON DISK agrees with it (stale regeneration, serializer round-trip drift between the generator's
/// in-memory model and what actually got written, etc.) -- exactly the "green test, dead game" gap flagged when this
/// bug was reopened after fix commit 3bdd8a0.
///
/// <para>
/// This test loads the real <c>content/sample.pkg</c> bytes from disk (no synthetic package, no mock) through the exact
/// same calls <c>LevelEditor.LoadFromResPath</c> / <c>StartPlaytest</c> make in production --
/// <see cref="EditableLevelReader.FromPackageBytes"/> then <see cref="EditableLevelSnapshot.ToResolvedLevel"/> -- and
/// asserts the spike tile <c>tools/SampleContent/Program.cs</c> places at cell (20,11) (<c>SpikeTileId</c>, wired to
/// <c>PredefinedBehaviors.HurtOnContact</c> with <c>amount=10</c>) survives into <see cref="ResolvedLevel.EffectiveTileBehaviors"/>
/// -- the exact enumeration <see cref="Behavior.BehaviorRuntime"/> (game/Behavior/BehaviorRuntime.cs, not referenced by this
/// Godot-free test project) consumes at runtime to register contact-scripted cells.
/// </para>
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

        // The exact production call sequence: LevelEditor._Ready -> LoadFromResPath -> EditableLevelReader.FromPackageBytes,
        // then LevelEditor.StartPlaytest -> EditableLevelSnapshot.ToResolvedLevel -> PlaytestOverlay.Start(level).
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

    // Walk up from the test assembly's output directory to the repo root rather than hard-coding a relative
    // path count -- robust to Debug/Release/net8.0 output-path changes, and fails loudly (not silently
    // skipped) if content/sample.pkg is ever moved or deleted.
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
