using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CombatSimulationTests
{
    [Fact]
    public void QueueDoesNotDamageUntilFixedTickAndDoesNotAutoRepeat()
    {
        var fixture = new Fixture();
        var initial = fixture.Target.Health;
        Assert.True(fixture.Combat.Queue(new(1), new(1, 999, Vector2.UnitY), 0));
        Assert.Equal(initial, fixture.Target.Health);
        fixture.Step();
        var action = Assert.Single(fixture.Combat.Events);
        Assert.True(action.Damage > 0);
        Assert.Equal(initial - action.Damage, fixture.Target.Health, 10);
        Assert.False(action.Critical);
        var remaining = fixture.Target.Health;
        for (var i = 0; i < 100; i++) fixture.Step();
        Assert.Equal(remaining, fixture.Target.Health);
        Assert.Empty(fixture.Combat.Events);
    }

    [Fact]
    public void ConeRangeAndDirectionDetermineHitsNotAnAssignedTarget()
    {
        var fixture = new Fixture();
        fixture.Attack(1, -Vector2.UnitY);
        Assert.False(Assert.Single(fixture.Combat.Events).TargetId.IsValid);
        fixture.AdvanceCooldown();
        fixture.MoveTarget(new(0, 3));
        fixture.Attack(2, Vector2.UnitY);
        Assert.Equal(0, Assert.Single(fixture.Combat.Events).Damage);
        fixture.AdvanceCooldown();
        fixture.MoveTarget(new(1.5f, 0));
        fixture.Attack(3, Vector2.UnitY);
        Assert.Equal(0, Assert.Single(fixture.Combat.Events).Damage);
        fixture.AdvanceCooldown();
        fixture.MoveTarget(new(0, 2));
        fixture.Attack(4, Vector2.UnitY);
        Assert.Equal(new NetworkEntityId(2), Assert.Single(fixture.Combat.Events).TargetId);
    }

    [Fact]
    public void WallsBlockDamageEvenInsideRange()
    {
        var fixture = new Fixture(range: 6, wall: true);
        fixture.Combat.Move(new(1), new(-1.5f, 0));
        fixture.Spatial.Move(new(1), new(-1.5f, 0));
        fixture.MoveTarget(new(1.5f, 0));
        fixture.Attack(1, Vector2.UnitX);
        Assert.Equal(0, Assert.Single(fixture.Combat.Events).Damage);
    }

    [Fact]
    public void DuplicateFutureClientTicksCooldownAndFloodCannotAccelerateAttacks()
    {
        var fixture = new Fixture();
        fixture.Attack(1, Vector2.UnitY);
        var health = fixture.Target.Health;
        Assert.False(fixture.Combat.Queue(new(1), new(1, 1, Vector2.UnitY), fixture.Tick));
        Assert.True(fixture.Combat.Queue(new(1), new(2, uint.MaxValue, Vector2.UnitY), fixture.Tick));
        for (uint sequence = 3; sequence < 1000; sequence++)
            Assert.False(fixture.Combat.Queue(new(1), new(sequence, uint.MaxValue, Vector2.UnitY), fixture.Tick));
        fixture.Step();
        Assert.Equal(health, fixture.Target.Health);
        Assert.Empty(fixture.Combat.Events);
        Assert.Single(fixture.Combat.Results);
        Assert.Equal(AttackOutcome.Cooldown, fixture.Combat.Results[new(1)].Outcome);
        Assert.False(fixture.Combat.Queue(new(1), new(2, 0, Vector2.UnitY), fixture.Tick));
    }

    [Fact]
    public void InvalidActorsDirectionsAndSequencesCannotDamage()
    {
        var fixture = new Fixture();
        Assert.False(fixture.Combat.Queue(new(999), new(1, 0, Vector2.UnitY), 0));
        Assert.False(fixture.Combat.Queue(new(2), new(1, 0, Vector2.UnitY), 0));
        Assert.False(fixture.Combat.Queue(new(1), new(0, 0, Vector2.UnitY), 0));
        Assert.False(fixture.Combat.Queue(new(1), new(1, 0, new(float.NaN, 0)), 0));
        Assert.False(fixture.Combat.Queue(new(1), new(2, 0, new(100, 0)), 0));
        fixture.Step();
        Assert.Empty(fixture.Combat.Events);
        Assert.Equal(fixture.Target.Stats.MaxHealth, fixture.Target.Health);
    }

    [Fact]
    public void NearestTargetTieUsesStableIdAndOnlyOneTargetIsDamaged()
    {
        var fixture = new Fixture();
        fixture.Combat.Add(new(3), fixture.Target.Position, CombatEntityKind.TrainingTarget);
        fixture.Spatial.Add(new(3), fixture.Target.Position);
        fixture.Attack(1, Vector2.UnitY);
        Assert.Equal(new NetworkEntityId(2), Assert.Single(fixture.Combat.Events).TargetId);
        Assert.Equal(fixture.Combat.Get(new(3)).Stats.MaxHealth, fixture.Combat.Get(new(3)).Health);
    }

    [Fact]
    public void CriticalRollIsServerOwnedAndHealthCannotGoBelowZero()
    {
        var fixture = new Fixture(roll: () => 0);
        fixture.Attack(1, Vector2.UnitY);
        Assert.True(Assert.Single(fixture.Combat.Events).Critical);
        for (uint sequence = 2; sequence <= 10; sequence++)
        {
            fixture.AdvanceCooldown();
            fixture.Attack(sequence, Vector2.UnitY);
        }
        Assert.Equal(0, fixture.Target.Health);
        Assert.False(Assert.Single(fixture.Combat.Events).TargetId.IsValid);
    }

    [Fact]
    public void PlayerIsNeverACombatTargetInTheTrainingPrototype()
    {
        var fixture = new Fixture();
        fixture.Combat.Add(new(3), new(0, 0.5f), CombatEntityKind.Player);
        fixture.Spatial.Add(new(3), new(0, 0.5f));
        fixture.Attack(1, Vector2.UnitY);
        Assert.Equal(new NetworkEntityId(2), Assert.Single(fixture.Combat.Events).TargetId);
        Assert.Equal(fixture.Combat.Get(new(3)).Stats.MaxHealth, fixture.Combat.Get(new(3)).Health);
    }

    [Fact]
    public void DisconnectDropsPendingAttackAndRuntimeCombatState()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Combat.Queue(new(1), new(1, 0, Vector2.UnitY), 0));
        fixture.Combat.Remove(new(1));
        fixture.Step();
        Assert.Empty(fixture.Combat.Events);
        Assert.Empty(fixture.Combat.Results);
    }

    [Fact]
    public void WorldOwnershipAndAoiIncludeTrainingTargetWithoutMovementSnapshot()
    {
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            catalog: ContentCatalogTests.Load(), combat: Options.Create(new CombatOptions { TargetX = -7, TargetZ = 1 }));
        var player = world.AddPlayer(42, new PlayerId(1));
        var view = new InterestView();
        world.UpdateInterest(42, view);
        Assert.Contains(world.TrainingTargetId, view.Entered);
        Assert.DoesNotContain(view.Snapshots, state => state.EntityId == world.TrainingTargetId);
        Assert.False(world.TryQueueAttack(999, new(1, 0, Vector2.UnitY)));
        Assert.True(world.TryQueueAttack(42, new(1, 0, Vector2.UnitY)));
        var position = player.Position;
        world.Simulate(0.05f);
        Assert.Equal(world.TrainingTargetId, Assert.Single(world.Combat!.Events).TargetId);
        Assert.Equal(position, player.Position);
    }

    private sealed class Fixture
    {
        public SpatialIndex Spatial { get; } = new(8);
        public CombatSimulation Combat { get; }
        public Combatant Target => Combat.Get(new(2));
        public uint Tick { get; private set; }

        public Fixture(float range = 2, bool wall = false, Func<double>? roll = null)
        {
            var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            json["weapons"]![0]!["range"] = range;
            var catalog = ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
            var options = new NavigationOptions();
            if (wall) options.BlockedAreas.Add(new BlockedAreaOptions { X = 14, Z = 10, Width = 2, Height = 10 });
            var grid = options.CreateGrid(new MovementOptions().ToSettings());
            Combat = new(catalog, Spatial, grid, new CombatOptions(), 20, 8, roll ?? (() => 1));
            var playerPosition = wall ? new Vector2(-1.5f, 0) : Vector2.Zero;
            var targetPosition = wall ? new Vector2(1.5f, 0) : new Vector2(0, 1);
            Combat.Add(new(1), playerPosition, CombatEntityKind.Player);
            Combat.Add(new(2), targetPosition, CombatEntityKind.TrainingTarget);
            Spatial.Add(new(1), playerPosition);
            Spatial.Add(new(2), targetPosition);
        }

        public void Step() => Combat.Simulate(0.05f, ++Tick);
        public void AdvanceCooldown() { for (var i = 0; i < 30; i++) Step(); }
        public void Attack(uint sequence, Vector2 direction)
        {
            Assert.True(Combat.Queue(new(1), new(sequence, Tick, direction), Tick));
            Step();
        }
        public void MoveTarget(Vector2 position) { Combat.Move(new(2), position); Spatial.Move(new(2), position); }
    }
}
