using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Server.WorldStory;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class NpcRespawnTests
{
    [Fact]public void FormerSpawnLifeResurrectsWithIndependentAiControlAndNormalCorpseCleanup()
    {
        var f=new Encounter(seconds:1);var world=f.World;var old=f.Npc.Id;
        var player=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {X=5,Z=0});
        f.Kill();f.Now+=1001;world.Simulate(.05f);var replacement=f.Npc.Id;
        Assert.NotEqual(old,replacement);Assert.True(world.Combat!.CanResurrect(old,1));
        Assert.True(world.Combat.Resurrect(default,old,1,20));world.Simulate(.05f);
        Assert.True(world.TryGetNpcActor(old,out var revived));Assert.NotSame(f.Npc,revived);
        Assert.Equal(old,revived.Id);Assert.Equal(replacement,f.Npc.Id);
        for(var i=0;i<25&&revived.Telegraph is null;i++)world.Simulate(.05f);
        Assert.Equal(player.EntityId,revived.TargetId);Assert.NotNull(revived.Telegraph);
        Assert.True(world.Combat.Knockback(old,Vector2.UnitY,1,4));var before=revived.Motion.Position;
        for(var i=0;i<6;i++)world.Simulate(.05f);Assert.True(Vector2.Distance(before,revived.Motion.Position)>.8f);
        var view=new InterestView();world.UpdateInterest(42,view);Assert.Contains(view.Snapshots,s=>s.EntityId==old);
        Assert.True(world.Combat.PayHealth(old,world.Combat.Get(old).Health,true));Assert.Empty(world.CaptureWorldNode().NpcRespawns!);
        f.Now+=60001;world.Simulate(.05f);Assert.False(world.Combat.TryGet(old,out _));Assert.False(world.TryGetNpcActor(old,out _));
        Assert.Equal(replacement,f.Npc.Id);Assert.True(world.Combat.Get(replacement).Health>0);
    }
    [Fact]public void ResurrectionBeforeSpawnReplacementCancelsThePendingReplacement()
    {
        var f=new Encounter(seconds:30);var id=f.Npc.Id;f.Kill();
        Assert.True(f.World.Combat!.Resurrect(default,id,1,20));Assert.Equal(NpcBehavior.Idle,f.Npc.Behavior);
        Assert.Empty(f.World.CaptureWorldNode().NpcRespawns!);f.Now+=30001;f.World.Simulate(.05f);
        Assert.Equal(id,f.Npc.Id);Assert.Equal(20,f.World.Combat.Get(id).Health);
    }
    internal sealed class Encounter
    {
        public long Now = 100000;
        public ServerWorld World { get; }
        public NpcSimulation Npc => World.Npc ?? World.Boss!;
        public Encounter(bool boss = false, int seconds = 30)
        {
            var actor = new NpcOptions { Enabled = true, SpawnId = "test_spawn", RespawnSeconds = seconds,
                DefinitionId = boss ? "test_boss" : "test_creature", X = 4, Z = 0, AggroRadius = 1 };
            World = new(Options.Create(new MovementOptions { SpawnX = 0 }), Options.Create(new InterestOptions()), catalog: ContentCatalogTests.Load(),
                npc: boss ? null : Options.Create(actor), boss: boss ? Options.Create(new BossOptions { Actor = actor }) : null,
                worldStory: Options.Create(new WorldStoryOptions { Enabled = true }),
                worldNodeDefinition: new WorldNodeDefinition { Key = "respawn-test", Interactive = false, OpeningCells = [] }, hitRoll: () => 0, criticalRoll: () => 1);
            World.NpcRespawnClock = () => Now;
            World.PvpClock=()=>Now;
        }
        public void Kill() => Assert.True(World.Combat!.ApplyAbilityDamage(Npc.Id, 1_000_000) > 0);
    }

    [Theory]
    [InlineData(false, 30)]
    [InlineData(true, 120)]
    public void DeathSchedulesOnceAndNewLifeGetsFullHealthHomeAndFreshIdentity(bool boss, int seconds)
    {
        var f = new Encounter(boss, seconds); var world = f.World;
        world.AddPlayer(42, new(1), world.CreateInitialCharacter() with { X = 0, Z = 0 });
        var view = new InterestView(); world.UpdateInterest(42, view);
        var oldId = f.Npc.Id;
        world.Combat!.Move(oldId, new(5, 0));
        f.Npc.Motion.Reset(new(5, 0), new(5, 0));
        world.Combat.Get(oldId).StunnedUntil = 99999;
        f.Kill();
        var pending = Assert.Single(world.CaptureWorldNode().NpcRespawns!);
        Assert.Equal(f.Now + seconds * 1000, pending.RespawnAtUnixMilliseconds);
        Assert.Equal(0, world.Combat.ApplyAbilityDamage(oldId, 100));
        Assert.Single(world.WorldNodeAudit);
        world.CommitWorldNode(world.WorldNodeRevision + 1);
        f.Now += seconds * 1000 - 1;
        world.Simulate(.05f);
        Assert.Equal(oldId, f.Npc.Id);
        Assert.Equal(NpcBehavior.Defeated, f.Npc.Behavior);
        Assert.False(world.WorldNodeDirty);
        f.Now++;
        world.Simulate(.05f);
        Assert.NotEqual(oldId, f.Npc.Id);
        Assert.Equal(seconds<60,world.Combat.TryGet(oldId,out _));
        var next = world.Combat.Get(f.Npc.Id);
        Assert.Equal(next.Stats.MaxHealth, next.Health);
        Assert.Equal(f.Npc.Home, next.Position);
        Assert.Equal(NpcBehavior.Idle, f.Npc.Behavior);
        Assert.False(f.Npc.TargetId.IsValid);
        Assert.Null(f.Npc.Telegraph); Assert.Null(f.Npc.Area);
        Assert.Equal(0, next.StunnedUntil);
        Assert.Empty(world.CaptureWorldNode().NpcRespawns!);
        world.UpdateInterest(42, view);
        if(seconds>=60)Assert.Contains(oldId,view.Left);else Assert.Contains(oldId,view.Entities);
        Assert.Contains(f.Npc.Id, view.Entered);
        if(seconds<60)
        {
            f.Now+=(60-seconds)*1000;world.Simulate(.05f);world.UpdateInterest(42,view);
            Assert.False(world.Combat.TryGet(oldId,out _));Assert.Contains(oldId,view.Left);
        }
        // A replacement life does not create an attack input or select a target.
        world.TryApplyMove(42, new(1, world.Tick, f.Npc.Home));
        for (var i = 0; i < 40; i++) world.Simulate(.05f);
        world.Simulate(.05f);
        Assert.Equal(next.Stats.MaxHealth, next.Health);
        f.Kill();
        Assert.Equal(f.Now + seconds * 1000, Assert.Single(world.CaptureWorldNode().NpcRespawns!).RespawnAtUnixMilliseconds);
    }

    [Fact]
    public async Task DeadlineSurvivesActualSqliteCheckpointAndRestartWithoutResettingWait()
    {
        var f = new Encounter(); var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        SavedWorldNode saved;
        await using (var lease = await store.OpenWorldAsync("respawn-test", new(), default))
        {
            f.World.RestoreWorldNode(lease.State, lease.Revision);
            f.Kill();
            await store.SaveWithWorldAsync([], new(lease, f.World.CaptureWorldNode(), f.World.WorldNodeAudit), default);
        }
        await using (var lease = await store.OpenWorldAsync("respawn-test", new(), default))
        {
            saved = lease.State;
            var restored = new Encounter { Now = 129999 };
            restored.World.RestoreWorldNode(saved, lease.Revision);
            Assert.Equal(0, restored.World.Combat!.Get(restored.Npc.Id).Health);
            restored.World.Simulate(.05f);
            Assert.Equal(0, restored.World.Combat.Get(restored.Npc.Id).Health);
            restored.Now++;
            restored.World.Simulate(.05f);
            Assert.True(restored.World.Combat.Get(restored.Npc.Id).Health > 0);
            Assert.True(restored.World.WorldNodeDirty);
        }
        var overdue = new Encounter { Now = 200000 };
        overdue.World.RestoreWorldNode(saved, 2);
        overdue.World.Simulate(.05f);
        Assert.True(overdue.World.Combat!.Get(overdue.Npc.Id).Health > 0);
    }

    [Fact]
    public void DisabledRespawnAndLegacySavesAreExplicitAndCorruptStatesFailClosed()
    {
        var disabled = new Encounter(seconds: 0);
        disabled.Kill(); disabled.Now += 10_000_000; disabled.World.Simulate(.05f);
        Assert.Equal(0, disabled.World.Combat!.Get(disabled.Npc.Id).Health);
        Assert.Equal(0, Assert.Single(disabled.World.CaptureWorldNode().NpcRespawns!).RespawnAtUnixMilliseconds);
        var fresh = new Encounter(); fresh.World.RestoreWorldNode(new(), 1);
        Assert.True(fresh.World.Combat!.Get(fresh.Npc.Id).Health > 0);
        Assert.Throws<InvalidDataException>(() => new SavedNpcRespawn(2, "test", 123).Validate());
        Assert.Throws<InvalidDataException>(() => new SavedNpcRespawn(1, "test", -1).Validate());
        Assert.Throws<InvalidDataException>(() => new SavedNpcRespawn(1, "../test", 123).Validate());
        Assert.Throws<InvalidDataException>(() => new Encounter(seconds: -1));
        Assert.Throws<InvalidDataException>(() => new SavedWorldNode { NpcRespawns = [new(1, "test", 123), new(1, "test", 456)] }.Validate());
        Assert.Throws<InvalidDataException>(() => new Encounter().World.RestoreWorldNode(new() { NpcRespawns = [new(1, "missing", 123)] }, 1));
        Assert.Throws<InvalidDataException>(() => SavedWorldNode.Deserialize("{\"Version\":1,\"Repairs\":0,\"Patrols\":0,\"StormRumor\":false,\"NpcRespawns\":null}"));
        Assert.Throws<System.Text.Json.JsonException>(() => SavedWorldNode.Deserialize("{\"Version\":1,\"Repairs\":0,\"Patrols\":0,\"StormRumor\":false,\"NpcRespawns\":[{\"Version\":1,\"SpawnId\":\"test\"}]}"));
    }
}
