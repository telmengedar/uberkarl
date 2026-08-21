using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>Pins that each <see cref="BehaviorScriptTemplates.For"/> starter template implements exactly the one handler the runtime dispatches for that subject kind.</summary>
[TestFixture]
public sealed class BehaviorScriptTemplatesTests
{
    [Test]
    public void Tile_CompilesWithoutQuarantine_AndItsOnContactHandlerFires()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("tile-1", "tile", "spike");

        var instance = ctx.Compile(subject, BehaviorScriptTemplates.For(BehaviorSubjectKind.Tile));
        Assert.That(instance.IsQuarantined, Is.False);

        var fired = ctx.Scheduler.DispatchContact("tile-1", new EventParty("player", string.Empty, new GridCell(0, 0)));

        Assert.That(fired, Is.True, "the tile template must implement onContact -- the only event F4 dispatches to a tile.");
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent("tile-1", "touched", true) }));
    }

    [Test]
    [Description("Negative control: the tile template must not also answer onUpdate.")]
    public void Tile_Template_DoesNotAnswerOnUpdate()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("tile-1", "tile", "spike");
        ctx.Compile(subject, BehaviorScriptTemplates.For(BehaviorSubjectKind.Tile));

        var fired = ctx.Scheduler.DispatchUpdate("tile-1", 0.1);

        Assert.That(fired, Is.False);
    }

    [Test]
    public void Trigger_CompilesWithoutQuarantine_AndItsOnEnterHandlerFires()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("trigger-1", "trigger", "zone");

        var instance = ctx.Compile(subject, BehaviorScriptTemplates.For(BehaviorSubjectKind.Trigger));
        Assert.That(instance.IsQuarantined, Is.False);

        var fired = ctx.Scheduler.DispatchEnter("trigger-1", new EventParty("player", string.Empty, new GridCell(1, 1)));

        Assert.That(fired, Is.True, "the trigger template must implement onEnter -- the first event F4 dispatches to a trigger.");
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent("trigger-1", "entered", true) }));
    }

    [Test]
    public void Object_CompilesWithoutQuarantine_AndItsOnUpdateHandlerFires()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "mover");

        var instance = ctx.Compile(subject, BehaviorScriptTemplates.For(BehaviorSubjectKind.Object));
        Assert.That(instance.IsQuarantined, Is.False);

        var fired = ctx.Scheduler.DispatchUpdate("obj-1", 0.5);

        Assert.That(fired, Is.True, "the object template must implement onUpdate (design #8769 §15 open question 1's non-blocking pick).");
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent("obj-1", "ticking", true) }));
    }

    [Test]
    [Description("Negative control: the object template must not also answer onSpawn.")]
    public void Object_Template_DoesNotAnswerOnSpawn()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("obj-1", "object", "mover");
        ctx.Compile(subject, BehaviorScriptTemplates.For(BehaviorSubjectKind.Object));

        var fired = ctx.Scheduler.DispatchSpawn("obj-1");

        Assert.That(fired, Is.False);
    }

    [Test]
    public void LevelScript_CompilesWithoutQuarantine_AndItsOnLevelStartHandlerFires()
    {
        var ctx = new BehaviorTestContext();
        var subject = ctx.CreateSubject("level-1", "level", string.Empty);

        var instance = ctx.Compile(subject, BehaviorScriptTemplates.For(BehaviorSubjectKind.LevelScript));
        Assert.That(instance.IsQuarantined, Is.False);

        var fired = ctx.Scheduler.DispatchLevelStart("level-1");

        Assert.That(fired, Is.True, "the level-script template must implement onLevelStart -- the only zero-argument event F4 dispatches to a level script at Configure time.");
        Assert.That(ctx.Intents.Drain(), Is.EqualTo(new BehaviorIntent[] { new SetStateIntent("level-1", "started", true) }));
    }

    [Test]
    public void For_UnknownKind_Throws()
    {
        Assert.That(() => BehaviorScriptTemplates.For((BehaviorSubjectKind)999), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
