using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Uberkarl.Behavior;
using Uberkarl.Content;

namespace Uberkarl.Editor.Tests;

/// <summary>
/// Proves the M6 acceptance fixture (<c>content/runaway-script.pkg</c>) is genuinely red content: read
/// through the same projection <c>LevelEditor.StartPlaytest</c> uses, its level script quarantines under the
/// runtime's own budgets. Stops the fixture from rotting silently if a budget or the parser changes
/// (design docs/architecture/quarantine-visibility.md §13.1 U-7).
/// </summary>
[TestFixture]
public sealed class RunawayScriptFixtureTests
{
    private const string SubjectId = "level-script";

    [Test]
    public void RunawayScriptPackage_LevelScript_QuarantinesUnderTheRuntimesOwnBudgets()
    {
        byte[] packageBytes = File.ReadAllBytes(FindPackagePath("runaway-script.pkg"));

        EditableLevel level = EditableLevelReader.FromPackageBytes(packageBytes);
        ResolvedLevel projection = EditableLevelSnapshot.ToResolvedLevel(level);

        Assert.That(projection.LevelScript, Is.Not.Null, "the fixture's level must bind a level script -- nothing else in it can quarantine.");
        Assert.That(projection.LevelScript!.IsScript, Is.True);

        BehaviorLoader loader = new BehaviorLoader(BehaviorScriptBudgets.DefaultBehavior(), BehaviorScriptBudgets.DefaultInit());
        BehaviorScheduler scheduler = new BehaviorScheduler();
        CompiledBehavior compiled = loader.CompileBinding(projection.LevelScript, new Dictionary<string, object>(), BehaviorScriptRole.Init);
        Assert.That(compiled.IsQuarantined, Is.False, "the fixture's onUpdate loop must not fire during init -- only a later dispatch should trip it.");
        scheduler.Register(new BehaviorInstance(SubjectId, compiled));

        bool fired = scheduler.DispatchUpdate(SubjectId, 0.016);

        Assert.That(fired, Is.False);
        Assert.That(scheduler.IsQuarantined(SubjectId), Is.True);
        Assert.That(compiled.QuarantineReason, Does.Contain("budget"),
            "the fixture must genuinely trip a budget breach, not some other quarantine cause -- otherwise A1 would pass for the wrong reason.");
    }

    private static string FindPackagePath(string fileName)
    {
        DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "content", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate content/{fileName} by walking up from the test directory " +
            $"'{TestContext.CurrentContext.TestDirectory}' -- repo layout changed?");
    }
}
