using NUnit.Framework;

namespace Uberkarl.Behavior.Tests;

/// <summary>
/// Direct (non-Pooscript) coverage of the reference facades' intent-recording contract (design #7704
/// §5.8/§8.1) — proves each action records the right closed-set intent, tagged with the right subject id,
/// and that reads never consult the intent buffer (design #8.5 — "handlers read a consistent snapshot").
/// </summary>
[TestFixture]
public sealed class BehaviorSubjectTests
{
    [Test]
    public void Actions_RecordIntents_TaggedWithSubjectId_InCallOrder()
    {
        var intents = new IntentBuffer();
        var subject = new BehaviorSubject("spike-1", "tile", "spike", intents);

        subject.MoveTo(new GridCell(3, 4));
        subject.MoveBy(1, -1);
        subject.SetState("armed", true);
        subject.SetGraphic("spike-broken");
        subject.After(500, "rearm");
        subject.Every(1000, "pulse");
        subject.Despawn();

        Assert.That(intents.Intents, Is.EqualTo(new BehaviorIntent[]
        {
            new MoveToCellIntent("spike-1", new GridCell(3, 4)),
            new MoveByIntent("spike-1", 1, -1),
            new SetStateIntent("spike-1", "armed", true),
            new SetGraphicIntent("spike-1", "spike-broken"),
            new ScheduleTimerIntent("spike-1", 500, "rearm", Repeating: false),
            new ScheduleTimerIntent("spike-1", 1000, "pulse", Repeating: true),
            new DespawnIntent("spike-1"),
        }));
    }

    [Test]
    public void MoveTo_Position_RecordsPositionIntent()
    {
        var intents = new IntentBuffer();
        var subject = new BehaviorSubject("obj-1", "object", "crate", intents);

        subject.MoveTo(new BehaviorVector2(12.5, 4));

        Assert.That(intents.Intents, Is.EqualTo(new BehaviorIntent[] { new MoveToPositionIntent("obj-1", new BehaviorVector2(12.5, 4)) }));
    }

    [Test]
    public void GetState_ReadsSeededValue_NeverTheIntentBuffer()
    {
        var subject = new BehaviorSubject("obj-1", "object", "gate", new IntentBuffer());
        subject.SeedState("open", false);

        Assert.That(subject.GetState("open"), Is.EqualTo(false));
        Assert.That(subject.GetState("missing"), Is.Null);

        subject.SetState("open", true); // records an intent -- must not change what GetState reads

        Assert.That(subject.GetState("open"), Is.EqualTo(false));
    }
}

[TestFixture]
public sealed class BehaviorLevelTests
{
    [Test]
    public void Object_ReturnsSeededSubject_ByName()
    {
        var intents = new IntentBuffer();
        var level = new BehaviorLevel(intents);
        var gate = new BehaviorSubject("obj-1", "object", "gate", intents);
        level.Objects["gate"] = gate;

        Assert.That(level.Object("gate"), Is.SameAs(gate));
        Assert.That(level.Object("missing"), Is.Null);
    }

    [Test]
    public void ObjectsNamed_ReturnsEveryMatch()
    {
        var intents = new IntentBuffer();
        var level = new BehaviorLevel(intents);
        var a = new BehaviorSubject("obj-1", "object", "patrol-point", intents);
        var b = new BehaviorSubject("obj-2", "object", "patrol-point", intents);
        level.Objects["obj-1"] = a;
        level.Objects["obj-2"] = b;

        var matches = level.ObjectsNamed("patrol-point");

        Assert.That(matches, Is.EquivalentTo(new IObjectFacade[] { a, b }));
    }

    [Test]
    public void TileAt_ReadsSeededTiles()
    {
        var level = new BehaviorLevel(new IntentBuffer());
        level.Tiles[(0, new GridCell(1, 1))] = "grass";

        Assert.That(level.TileAt(0, new GridCell(1, 1)), Is.EqualTo("grass"));
        Assert.That(level.TileAt(0, new GridCell(2, 2)), Is.Null);
    }

    [Test]
    public void Actions_RecordIntents_TaggedWithLevelSubjectId()
    {
        var intents = new IntentBuffer();
        var level = new BehaviorLevel(intents);

        level.SetTile(0, new GridCell(1, 1), "lava");
        level.Spawn("objects/coin.json", new GridCell(2, 2));
        level.SetState("cleared", true);
        level.Message("gate-1", "open", null);

        Assert.That(intents.Intents, Is.EqualTo(new BehaviorIntent[]
        {
            new SetTileIntent(BehaviorSubjectIds.Level, 0, new GridCell(1, 1), "lava"),
            new SpawnIntent(BehaviorSubjectIds.Level, "objects/coin.json", new GridCell(2, 2)),
            new SetStateIntent(BehaviorSubjectIds.Level, "cleared", true),
            new MessageIntent(BehaviorSubjectIds.Level, "gate-1", "open", null),
        }));
    }
}

[TestFixture]
public sealed class BehaviorPlayerTests
{
    [Test]
    public void Actions_RecordIntents_TaggedWithPlayerSubjectId()
    {
        var intents = new IntentBuffer();
        var player = new BehaviorPlayer(intents);

        player.Hurt(10);
        player.Heal(5);
        player.Teleport(new GridCell(0, 0));
        player.SetSpawn("checkpoint-1");
        player.SetPhysics("jumpSpeed", 420.0);

        Assert.That(intents.Intents, Is.EqualTo(new BehaviorIntent[]
        {
            new HurtIntent(BehaviorSubjectIds.Player, 10),
            new HealIntent(BehaviorSubjectIds.Player, 5),
            new TeleportIntent(BehaviorSubjectIds.Player, new GridCell(0, 0)),
            new SetSpawnIntent(BehaviorSubjectIds.Player, "checkpoint-1"),
            new SetPhysicsIntent(BehaviorSubjectIds.Player, "jumpSpeed", 420.0),
        }));
    }

    [Test]
    public void Reads_ReflectHostSeededFields()
    {
        var player = new BehaviorPlayer(new IntentBuffer())
        {
            Position = new BehaviorVector2(10, 20),
            Velocity = new BehaviorVector2(0, -5),
            IsOnGround = true,
        };
        player.State["lives"] = 3;

        Assert.That(player.Position, Is.EqualTo(new BehaviorVector2(10, 20)));
        Assert.That(player.Velocity, Is.EqualTo(new BehaviorVector2(0, -5)));
        Assert.That(player.IsOnGround, Is.True);
        Assert.That(player.GetState("lives"), Is.EqualTo(3));
    }
}
