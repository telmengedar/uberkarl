using NUnit.Framework;
using Uberkarl.Packages;

namespace Uberkarl.Editor.Tests;

/// <summary>Pins <see cref="ScriptResourcePaths"/>' slug-allocation boundaries: collision, empty input, round-trip.</summary>
[TestFixture]
public sealed class ScriptResourcePathsTests
{
    [Test]
    public void ScriptPath_ReturnsScriptsSlashSlugDotPoo()
    {
        Assert.That(ScriptResourcePaths.ScriptPath("door-opener"), Is.EqualTo(ResourcePath.Create("scripts/door-opener.poo")));
    }

    [Test]
    public void Slugify_OrdinaryName_LowercasesAndHyphenates()
    {
        Assert.That(ScriptResourcePaths.Slugify("Door Opener"), Is.EqualTo("door-opener"));
    }

    [Test]
    [Description("A name that slugs to empty must not reuse LevelResourcePaths' 'level' fallback.")]
    public void Slugify_NameWithNoAlphanumericContent_FallsBackToScript_NotLevel()
    {
        Assert.That(ScriptResourcePaths.Slugify("###"), Is.EqualTo("script"));
    }

    [Test]
    public void Slugify_BlankName_FallsBackToScript()
    {
        Assert.That(ScriptResourcePaths.Slugify("   "), Is.EqualTo("script"));
    }

    [Test]
    [Description("A name that legitimately slugs to the word 'level' must be left alone.")]
    public void Slugify_NameThatLegitimatelySlugsToLevel_IsNotOverridden()
    {
        Assert.That(ScriptResourcePaths.Slugify("Level!"), Is.EqualTo("level"));
    }

    [Test]
    [Description("Two authors naming a script the same thing must land on different slugs.")]
    public void UniqueSlug_WhenBaseTaken_AppendsDashTwo()
    {
        var result = ScriptResourcePaths.UniqueSlug("door-opener", slug => slug == "door-opener");

        Assert.That(result, Is.EqualTo("door-opener-2"));
    }

    [Test]
    public void UniqueSlug_WhenFree_ReturnsBaseUnchanged()
    {
        var result = ScriptResourcePaths.UniqueSlug("door-opener", _ => false);

        Assert.That(result, Is.EqualTo("door-opener"));
    }

    [Test]
    public void SlugFromScriptPath_RoundTripsScriptPath()
    {
        var path = ScriptResourcePaths.ScriptPath("door-opener");

        Assert.That(ScriptResourcePaths.SlugFromScriptPath(path), Is.EqualTo("door-opener"));
    }

    [Test]
    public void SlugFromScriptPath_NonMatchingPath_ReturnsNull()
    {
        Assert.That(ScriptResourcePaths.SlugFromScriptPath(ResourcePath.Create("levels/demo.json")), Is.Null);
    }
}
