using NUnit.Framework;

namespace Uberkarl.Editor.Tests;

/// <summary>Covers the script source editor's validation-footer text formatting.</summary>
[TestFixture]
public sealed class ScriptValidationFooterTextTests
{
    [Test]
    public void Format_NullReason_ReturnsTheCleanCompileMessage()
    {
        string text = ScriptValidationFooterText.Format(null);

        Assert.That(text, Is.EqualTo("compiles"));
    }

    [Test]
    public void Format_AReason_ReturnsItVerbatim()
    {
        string text = ScriptValidationFooterText.Format("parse error: unexpected end of input");

        Assert.That(text, Is.EqualTo("parse error: unexpected end of input"));
    }

    [Test]
    [Description("DiVoid #9076: an empty (not null) reason must render blank, not fall back to the clean-compile message.")]
    public void Format_EmptyStringReason_ReturnsItVerbatim()
    {
        string text = ScriptValidationFooterText.Format(string.Empty);

        Assert.That(text, Is.EqualTo(string.Empty));
    }
}
