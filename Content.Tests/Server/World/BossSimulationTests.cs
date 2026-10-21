using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class BossSimulationTests
{
    private sealed class Fixture
    {
        public ServerWorld World { get; }
        public NpcSimulation Boss => World.Boss!;
        public ServerPlayer Player { get; }
        public List<AttackEvent> Events { get; } = new();
        public List<AbilityHit> Hits {get;}=new();
        public Fixture(BossOptions? options = null, ContentCatalog? catalog = null, NavigationOptions? navigation = null)
        {
            World = new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
                Options.Create(navigation ?? new NavigationOptions()), catalog ?? ContentCatalogTests.Load(),
                boss: Options.Create(options ?? new BossOptions { Actor = new NpcOptions { Enabled = true, DefinitionId = "test_boss", X = -3, Z = 0 } }), hitRoll: () => 0, criticalRoll: () => 1);
            Player = AddPlayer(42, 1);
        }
        public ServerPlayer AddPlayer(int connection, ulong id)
        {
            var player = World.AddPlayer(connection, new(id));
            var actor = World.Combat!.Get(player.EntityId);
            // Isolate boss damage/cancellation assertions from the independently tested passive heal.
            World.Combat.ApplyEquipment(player.EntityId, (actor.Stats with { HealthRecovery = 0 }, actor.Weapon, actor.AttackInterval));
            return player;
        }
        public void Step() { World.Simulate(0.05f); Events.AddRange(World.Combat!.Events);Hits.AddRange(World.Abilities!.Hits); }
        public void Wait(Func<bool> predicate)
        {
            for (var i = 0; i < 400 && !predicate(); i++) Step();
            Assert.True(predicate(), "Expected boss phase did not occur.");
        }
        public double Health => World.Combat!.Get(Player.EntityId).Health;
    }

    [Fact]
    public void BossAlternatesConeAndLockedAreaAndConsumesCooldownOnce()
    {
        var f = new Fixture();
        f.Wait(() => f.Boss.Telegraph is { RemainingSeconds: > 0 });
        Assert.Null(f.Boss.Area);
        f.Wait(() => f.Boss.Area is { Phase: NpcAreaPhase.Telegraph });
        var area = f.Boss.Area!.Value;
        var hp = f.Health;
        for (var i = 0; i < 10; i++) { f.Step(); Assert.Equal(hp, f.Health); Assert.Equal(area.Center, f.Boss.Area!.Value.Center); }
        f.Wait(() => f.Boss.Area is { Phase: NpcAreaPhase.Impact });
        Assert.True(f.Health < hp);
        Assert.Single(f.Hits,value=>value.ActorId==f.Boss.Id&&value.TargetId==f.Player.EntityId);
        var nextBasic=false;
        for (var i = 0; i < 20; i++)
        {f.Step();nextBasic|=f.Boss.Telegraph is {RemainingSeconds:>0} cone&&cone.Sequence>area.Sequence;}
        Assert.Single(f.Hits,value=>value.ActorId==f.Boss.Id&&value.TargetId==f.Player.EntityId);
        Assert.Equal(NpcAreaPhase.Finished, f.Boss.Area!.Value.Phase);
        Assert.True(nextBasic);
    }

    [Fact]
    public void LeavingAreaDuringWindupAvoidsDamageWithoutMovingTheCircle()
    {
        var f = new Fixture(); f.Wait(() => f.Boss.Area is { Phase: NpcAreaPhase.Telegraph });
        var center = f.Boss.Area!.Value.Center;
        var health = f.Health;
        Assert.True(f.World.TryQueueAbility(42, new(1, 0, 3, -Vector2.UnitY), 0));
        f.Wait(() => f.Boss.Area is { Phase: NpcAreaPhase.Impact });
        Assert.Equal(center, f.Boss.Area!.Value.Center);
        Assert.True(Vector2.Distance(center, f.Player.Position) > f.Boss.Area.Value.Radius);
        Assert.Equal(health, f.Health);
    }

    [Fact]
    public void AreaHitsEachPlayerOnceButNeverNpcAndIgnoresApplicationReplay()
    {
        var f = new Fixture(); var second = f.AddPlayer(43, 2);
        f.World.TryApplyMove(43, new(1, 0, f.Player.Position));
        f.Wait(() => f.Boss.Area is { Phase: NpcAreaPhase.Telegraph });
        var area = f.Boss.Area!.Value;
        var health = f.Health; var otherHealth = f.World.Combat!.Get(second.EntityId).Health;
        var bossHealth = f.World.Combat.Get(f.Boss.Id).Health;
        f.Wait(() => f.Boss.Area is { Phase: NpcAreaPhase.Impact });
        var hits = f.Hits.Where(value => value.ActorId == f.Boss.Id).ToArray();
        Assert.Equal(2, hits.Length);
        Assert.Equal(2, hits.Select(value => value.TargetId).Distinct().Count());
        Assert.Equal(health - f.Health, otherHealth - f.World.Combat.Get(second.EntityId).Health, 8);
        Assert.Equal(bossHealth, f.World.Combat.Get(f.Boss.Id).Health);
        Assert.False(f.World.Combat.ExecuteNpcArea(f.Boss.Id, area.Sequence, area.Center,
            ContentCatalogTests.Load().Abilities["boss_ground_area"], f.World.Tick));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DefeatOrDisconnectDuringAreaCancelsIt(bool disconnect)
    {
        var f = new Fixture(); f.Wait(() => f.Boss.Area is { Phase: NpcAreaPhase.Telegraph });
        var seq = f.Boss.Area!.Value.Sequence; var hp = f.Health;
        if (disconnect) f.World.RemovePlayer(42);
        else f.World.Combat!.ApplyAbilityDamage(f.Boss.Id, 100000);
        for (var i = 0; i < 60; i++) f.Step();
        Assert.Equal(NpcAreaPhase.Finished, f.Boss.Area!.Value.Phase);
        Assert.DoesNotContain(f.Events, action => action.AttackerId == f.Boss.Id && action.Sequence == seq);
        Assert.DoesNotContain(f.Hits,action=>action.ActorId==f.Boss.Id);
        if (!disconnect) { Assert.Equal(hp, f.Health); Assert.Equal(NpcBehavior.Defeated, f.Boss.Behavior); }
        else Assert.Equal(f.Boss.Home, f.Boss.Motion.Position);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FixedHealthBossCanBeDefeatedSoloOrTogetherWithoutAutoscale(bool together)
    {
        // NC: this encounter test uses an established build, not the new level-one starting stats.
        var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        var stats = json["creatures"]![0]!["stats"]!.AsObject();
        foreach (var key in stats.Select(pair => pair.Key).ToArray()) stats[key] = 10;
        stats["vitality"]=100;
        var f = new Fixture(catalog: ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString())));
        var hp = f.World.Combat!.Get(f.Boss.Id).Stats.MaxHealth;
        if (together) f.AddPlayer(43, 2);
        Assert.Equal(hp, f.World.Combat.Get(f.Boss.Id).Stats.MaxHealth, 8);
        Assert.Equal(hp, f.World.Combat.Get(f.Boss.Id).Health);
        for (uint seq = 1; seq <= 40 && f.World.Combat.Get(f.Boss.Id).Health > 0; seq++)
        {
            f.World.TryQueueAttack(42, new(seq, 0, Vector2.UnitX));
            if (together) f.World.TryQueueAttack(43, new(seq, 0, Vector2.UnitY));
            for (var tick = 0; tick < 20; tick++) f.Step();
        }
        Assert.Equal(0, f.World.Combat.Get(f.Boss.Id).Health);
        Assert.Equal(NpcBehavior.Defeated, f.Boss.Behavior);
        Assert.True(f.Health > 0);
    }

    [Fact]
    public void AreaDamageRequiresLineOfSightToEachPlayer()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["abilities"]![3]!["radius"]=10;
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));var spatial = new SpatialIndex(8);
        var grid = new NavigationOptions
        {
            BlockedAreas = new() { new BlockedAreaOptions { X = 14, Z = 10, Width = 2, Height = 10 } }
        }.CreateGrid(new MovementOptions().ToSettings());
        var combat = new Content.Server.Combat.CombatSimulation(catalog, spatial, grid, new CombatOptions(), 20, 8);
        combat.Add(new(1), new(-2, 0), CombatEntityKind.Boss, "test_boss"); spatial.Add(new(1), new(-2, 0));
        combat.Add(new(2), new(1.5f, 0), CombatEntityKind.Player); spatial.Add(new(2), new(1.5f, 0));
        var abilities=new Content.Server.Combat.AbilitySimulation(catalog,combat,spatial,grid,new CombatOptions(),8,(_,_,_)=>false);
        abilities.AddActor(new(1),catalog.Creatures["test_boss"]);
        var ability = catalog.Abilities["boss_ground_area"];
        combat.Simulate(0.05f, 1);
        Assert.True(combat.ExecuteNpcArea(new(1), 1, new(-2, 0), ability, 1));
        combat.Simulate(2,2);
        Assert.Empty(combat.Events);
        Assert.Empty(abilities.Hits);
        Assert.Equal(combat.Get(new(2)).Stats.MaxHealth, combat.Get(new(2)).Health);
    }

    [Fact]
    public void InvalidAreaProfilesFailBeforeAcceptingConnections()
    {
        Assert.Throws<ArgumentException>(() => new Fixture(new BossOptions { Actor = new NpcOptions { Enabled = true }, AreaAbilityId = "missing" }));
        var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        json["abilities"]![3]!["action"]!["preparation"]!["seconds"] = 100;
        var catalog = ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Throws<ArgumentException>(() => new Fixture(catalog: catalog));
    }
}
