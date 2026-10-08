using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class AbilitySimulationTests
{
    [Fact]
    public void ProjectileCastsOnTicksConsumesManaHitsOnceAndDoesNotRepeat()
    {
        var f = new Fixture();
        var hp = f.Health;
        Assert.True(f.Queue(1, 1, Vector2.UnitY));
        Assert.Equal(hp, f.Health);
        f.Step();
        Assert.Equal(AbilityOutcome.Accepted, f.Result);
        Assert.Equal(50, f.Abilities.Loadout(f.Player.EntityId, f.World.Tick).Mana, 8);
        Assert.Equal(AbilityPhase.Telegraph, Assert.Single(f.Abilities.ActiveStates).Phase);
        Assert.Equal(hp, f.Health);
        for (var i = 0; i < 80; i++) f.Step();
        Assert.Single(f.Hits);
        Assert.True(f.Health < hp);
        Assert.Empty(f.Abilities.ActiveStates);
        Assert.Equal(hp - f.Hits[0].Damage, f.Health, 8);
    }

    [Fact]
    public void CastBusyAndCooldownCannotBeBypassedBySequenceOrReplay()
    {
        var f = new Fixture();
        f.Queue(1, 1, Vector2.UnitY); f.Step();
        Assert.False(f.Queue(1, 1, Vector2.UnitY));
        f.Queue(2, 2, new(-7, 3)); f.Step();
        Assert.Equal(AbilityOutcome.Busy, f.Result);
        for (var i = 0; i < 15; i++) f.Step();
        f.Queue(3, 1, Vector2.UnitY); f.Step();
        Assert.Equal(AbilityOutcome.Cooldown, f.Result);
        Assert.Equal(50, f.Abilities.Loadout(f.Player.EntityId, f.World.Tick).Mana, 8);
    }

    [Theory]
    [InlineData(999, 0, 1, false)]
    [InlineData(1, 100, 0, false)]
    [InlineData(2, 15, 15, false)]
    [InlineData(1, 0, 1, true)]
    public void UnknownInvalidAndFutureIntentCannotSpendResources(int id, float x, float z, bool future)
    {
        var f = new Fixture();
        f.World.TryQueueAbility(42, new(1, future ? 1000u : 0u, (ushort)id, new(x, z)), 100000);
        f.Step();
        Assert.NotEqual(AbilityOutcome.Accepted, f.Result);
        Assert.Equal(55, f.Abilities.Loadout(f.Player.EntityId, f.World.Tick).Mana, 8);
        Assert.Empty(f.Abilities.ActiveStates);
        Assert.Empty(f.Hits);
        Assert.False(f.World.TryQueueAbility(999, new(1, 0, 1, Vector2.UnitY), 0));
    }

    [Fact]
    public void GroundAreaWaitsForCastAndDoesNotHurtPlayers()
    {
        var f = new Fixture();
        var second = f.World.AddPlayer(43, new(2));
        f.World.Combat!.Move(second.EntityId, new(-7, 3));
        var hp = f.Health;
        f.Queue(1, 2, new(-7, 3)); f.Step();
        for (var i = 0; i < 8; i++) f.Step();
        Assert.Equal(hp, f.Health);
        for (var i = 0; i < 50; i++) f.Step();
        Assert.Single(f.Hits);
        Assert.True(f.Health < hp);
        Assert.Equal(f.World.Combat.Get(second.EntityId).Stats.MaxHealth, f.World.Combat.Get(second.EntityId).Health);
        Assert.Equal(45, f.Abilities.Loadout(f.Player.EntityId, f.World.Tick).Mana, 8);
    }

    [Fact]
    public void DashMovesOnFixedTicksCancelsRouteAndCannotAssignCoordinates()
    {
        var f = new Fixture();
        var start = f.Player.Position;
        f.Queue(1, 3, Vector2.UnitX); f.Step();
        Assert.Equal(start, f.Player.Position);
        f.Step();
        Assert.Equal(start + new Vector2(0.75f, 0), f.Player.Position);
        for (var i = 0; i < 8; i++) f.Step();
        Assert.Equal(start + new Vector2(3, 0), f.Player.Position);
        Assert.Empty(f.Abilities.ActiveStates);
        var view = new InterestView(); f.World.UpdateInterest(42, view);
        Assert.Equal(1u, Assert.Single(view.Snapshots).LastAbilitySequence);
        Assert.Equal(0, Assert.Single(view.Snapshots).DashSpeed);
    }

    [Fact]
    public void DisconnectAndFloodHaveBoundedCleanup()
    {
        var f = new Fixture();
        f.Queue(1, 1, Vector2.UnitY);
        for (uint seq = 2; seq < 1000; seq++) Assert.False(f.Queue(seq, 1, Vector2.UnitY));
        f.Step();
        Assert.Equal(AbilityOutcome.RateLimited, f.Result);
        Assert.Empty(f.Abilities.ActiveStates);
        Assert.Single(f.Abilities.Results);
        f.Queue(1000, 1, Vector2.UnitY); f.Step();
        f.World.RemovePlayer(42);
        f.Step();
        Assert.Empty(f.Abilities.Results);
        Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]
    public void CapacityRejectsWithoutSpendingAndAoiLifecycleDoesNotLeakEffects()
    {
        var f = new Fixture(new CombatOptions { MaxAbilityEffects = 1 });
        var second = f.World.AddPlayer(43, new(2));
        f.Queue(1, 1, Vector2.UnitY); f.Step();
        var view = new InterestView(); var effects = new AbilityInterestView();
        f.World.UpdateInterest(43, view); f.World.UpdateAbilityInterest(43, view, effects);
        Assert.Single(effects.Changes);
        f.World.TryQueueAbility(43, new(1, 0, 2, new(-7, 3)), 0); f.Step();
        Assert.Equal(AbilityOutcome.Capacity, f.Abilities.Results[second.EntityId].Outcome);
        Assert.Equal(55, f.Abilities.Loadout(second.EntityId, f.World.Tick).Mana, 8);
        for (var i = 0; i < 50; i++) f.Step();
        f.World.UpdateInterest(43, view); f.World.UpdateAbilityInterest(43, view, effects);
        Assert.Equal(AbilityPhase.Finished, Assert.Single(effects.Changes).Phase);
    }

    [Fact]
    public void MeasuredLatencyIsCappedAndOldObservationsDoNotEarnCompensation()
    {
        static Vector2 FirstFlying(int rtt, bool stale)
        {
            var f = new Fixture();
            // Fire away from the target so only travel distance is measured.
            for (var i = 0; i < 10; i++) f.Step();
            f.World.TryQueueAbility(42, new(1, stale ? 0 : f.World.Tick, 1, Vector2.UnitX), rtt);
            f.Step();
            for (var i = 0; i < 20; i++)
            {
                f.Step();
                if (f.Abilities.ActiveStates.Count != 0 && f.Abilities.ActiveStates[0].Phase == AbilityPhase.Flying)
                    return f.Abilities.ActiveStates[0].Position;
            }
            throw new Exception("Projectile did not release.");
        }
        var baseline = FirstFlying(0, false);
        var capped = FirstFlying(200, false);
        Assert.Equal(capped, FirstFlying(int.MaxValue, false));
        Assert.Equal(baseline, FirstFlying(int.MaxValue, true));
        Assert.InRange(Vector2.Distance(baseline, capped), 0, 0.801f);
    }

    [Fact]
    public void ExhaustedManaRejectsCastWithoutNegativeResourceOrCooldown()
    {
        var f = new Fixture();
        for (uint sequence = 1; sequence <= 5; sequence++)
        {
            f.Queue(sequence, 2, new(-7, 3)); f.Step();
            Assert.Equal(AbilityOutcome.Accepted, f.Result);
            for (var i = 0; i < 110; i++) f.Step();
        }
        f.Queue(6, 2, new(-7, 3)); f.Step();
        Assert.Equal(AbilityOutcome.NoMana, f.Result);
        var loadout = f.Abilities.Loadout(f.Player.EntityId, f.World.Tick);
        Assert.Equal(5, loadout.Mana, 8);
        Assert.Equal(0, loadout.Abilities.Single(slot => slot.Id == 2).ReadyInSeconds);
    }

    [Fact]
    public void ProjectileAndAreaCannotDamageThroughWall()
    {
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            Options.Create(new NavigationOptions
            {
                BlockedAreas = new() { new BlockedAreaOptions { X = 14, Z = 10, Width = 2, Height = 10 } }
            }), ContentCatalogTests.Load(), Options.Create(new CombatOptions { TargetX = 1.5f, TargetZ = 0 }));
        var player = world.AddPlayer(42, new(1));
        var hp = world.Combat!.Get(world.TrainingTargetId).Health;
        world.TryQueueAbility(42, new(1, 0, 1, Vector2.UnitX), int.MaxValue);
        for (var i = 0; i < 80; i++)
        {
            world.Simulate(0.05f);
            Assert.Empty(world.Abilities!.Hits);
        }
        world.TryQueueAbility(42, new(2, 0, 2, new(1.5f, 0)), 0);
        world.Simulate(0.05f);
        Assert.Equal(AbilityOutcome.InvalidAim, world.Abilities!.Results[player.EntityId].Outcome);
        Assert.Equal(hp, world.Combat.Get(world.TrainingTargetId).Health);
    }

    [Fact]
    public void UnsupportedContentFailsBeforeAPlayerConnects()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        json["abilities"]![0]!["speed"] = 0.001;
        var catalog = Content.Server.Data.ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Throws<ArgumentException>(() => new ServerWorld(Options.Create(new MovementOptions()),
            Options.Create(new InterestOptions()), catalog: catalog));
    }

    private sealed class Fixture
    {
        public ServerWorld World { get; }
        public ServerPlayer Player { get; }
        public Content.Server.Combat.AbilitySimulation Abilities => World.Abilities!;
        public double Health => World.Combat!.Get(World.TrainingTargetId).Health;
        public List<AbilityHit> Hits { get; } = new();
        public AbilityOutcome Result => Abilities.Results[Player.EntityId].Outcome;
        public Fixture(CombatOptions? options = null)
        {
            World = new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
                catalog: ContentCatalogTests.Load(), combat: Options.Create(options ?? new CombatOptions()));
            Player = World.AddPlayer(42, new(1));
        }
        public bool Queue(uint seq, ushort id, Vector2 aim) => World.TryQueueAbility(42, new(seq, 0, id, aim), 0);
        public void Step() { World.Simulate(0.05f); Hits.AddRange(Abilities.Hits); }
    }
}
