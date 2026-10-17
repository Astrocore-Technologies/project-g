using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Server.Regions;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CompanionPersistenceTests
{
    private sealed class Fight
    {
        internal readonly NetworkEntityId Owner = new(1), Echo = new(2);
        internal readonly ContentCatalog Catalog;
        internal readonly CombatSimulation Combat;
        internal readonly AbilitySimulation Abilities;
        internal uint Tick;
        internal Fight(Guid instance)
        {
            var data = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            data["effects"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"echo_test_dot","durationSeconds":5,"disposition":"Harmful",
                 "periodic":{"kind":"Damage","intervalSeconds":1,"power":{"flat":3}}}
                """));
            Catalog = ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
            var grid = new NavigationOptions().CreateGrid(new MovementOptions().ToSettings()); var spatial = new SpatialIndex(8);
            Combat = new(Catalog, spatial, grid, new CombatOptions(), 20, 8, () => 1, () => 0);
            Abilities = new(Catalog, Combat, spatial, grid, new CombatOptions(), 8, (_, _, _) => false);
            Combat.Add(Owner, new(-7, 0), CombatEntityKind.Player);
            Combat.Add(Echo, new(-6, 0), CombatEntityKind.Echo, "test_adventurer");
            spatial.Add(Owner, new(-7, 0)); spatial.Add(Echo, new(-6, 0));
            Combat.BindCompanion(Echo, Owner, instance); Abilities.AddActor(Echo, Catalog.Creatures["test_adventurer"]);
            var actor = Combat.Get(Echo);
            Combat.ApplyEquipment(Echo, (actor.BaseDerivedStats with
            {HealthRecovery = 0, ManaRecovery = 0, StaminaRecovery = 0, PhysicalDefense = 0}, actor.Weapon, actor.AttackInterval));
        }
        internal void Step(float delta) => Combat.Simulate(delta, ++Tick);
        internal SavedEchoCombat Capture(byte? slot = 1) => CompanionRuntimeState.Capture(Combat, Abilities, Echo,
            "test_guardian_echo", Catalog.Creatures["test_adventurer"].Stats, slot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void RecallAndResummonKeepIndependentResourcesCooldownsAcrAndFacing()
    {
        var id = Guid.NewGuid(); var f = new Fight(id); var actor = f.Combat.Get(f.Echo);
        Assert.True(f.Abilities.QueueAiAbility(f.Echo, new(1, 0, 2, new(-5, 0)), 0)); f.Step(.05f);
        Assert.True(f.Combat.ApplyStatusControl(default, f.Echo, 10, 10, CrowdControlKind.Stun, .5));
        actor.Health = 51; actor.Resources.Stamina = 31; actor.Facing = Vector2.UnitX;
        var saved = f.Capture(null); var effects = CompanionRuntimeState.CaptureEffects(f.Combat, f.Echo);
        var next = new Fight(id); CompanionRuntimeState.Restore(next.Combat, next.Abilities, next.Echo, saved, effects, .2);
        var restored = next.Combat.Get(next.Echo);
        Assert.Equal(SavedEchoAvailability.Ready, saved.Availability); Assert.Null(saved.ActiveSlot);
        Assert.Equal(51, restored.Health); Assert.Equal(saved.Mana, restored.Resources.Mana); Assert.Equal(31, restored.Resources.Stamina);
        Assert.Equal(Vector2.UnitX, restored.Facing); Assert.Null(restored.Action);
        Assert.Equal(saved.Cooldowns.Single(c => c.AbilityId == "test_ground_area").Seconds - .2,
            next.Abilities.CaptureCooldowns(next.Echo).Single(c => c.AbilityId == "test_ground_area").Seconds, 8);
        Assert.True(restored.Controls.Has(CrowdControlKind.Stun));
        Assert.Equal(.5, restored.Controls.Ledger.Snapshot(ControlCategory.Hard, next.Combat.Time).Used);
        Assert.Equal(next.Combat.Get(next.Owner).Stats.MaxHealth, next.Combat.Get(next.Owner).Health);
    }

    [Fact]
    public void OfflineEffectsExpireWithoutBacklogAndPreserveOriginalTickRhythm()
    {
        var id = Guid.NewGuid(); var f = new Fight(id);
        f.Combat.ApplyEffect(default, f.Echo, "echo_test_dot"); f.Step(.25f);
        var saved = f.Capture(); var effects = CompanionRuntimeState.CaptureEffects(f.Combat, f.Echo);
        var next = new Fight(id); CompanionRuntimeState.Restore(next.Combat, next.Abilities, next.Echo, saved, effects, 2);
        Assert.Equal(saved.Health, next.Combat.Get(next.Echo).Health);
        next.Step(.74f); Assert.Equal(saved.Health, next.Combat.Get(next.Echo).Health);
        next.Step(.02f); Assert.Equal(saved.Health - 3, next.Combat.Get(next.Echo).Health, 8);
        var expired = new Fight(id); CompanionRuntimeState.Restore(expired.Combat, expired.Abilities, expired.Echo, saved, effects, 8);
        Assert.Empty(expired.Combat.Get(expired.Echo).Effects); Assert.Equal(saved.Health, expired.Combat.Get(expired.Echo).Health);
    }

    [Fact]
    public void RestartDuringPreparationCancelsFreelyAndNeverCreatesAnEffect()
    {
        var id = Guid.NewGuid(); var f = new Fight(id);
        Assert.True(f.Abilities.QueueAiAbility(f.Echo, new(1, 0, 2, new(-5, 0)), 0)); f.Step(.05f);
        var saved = f.Capture(); Assert.True(f.Combat.Get(f.Echo).IsCasting);
        var next = new Fight(id); CompanionRuntimeState.Restore(next.Combat, next.Abilities, next.Echo, saved, [], 2);
        Assert.Null(next.Combat.Get(next.Echo).Action); Assert.Equal(saved.Mana, next.Abilities.Mana(next.Echo));
        Assert.Equal(0, next.Abilities.CaptureCooldowns(next.Echo).Single(c => c.AbilityId == "test_ground_area").Seconds);
        Assert.Empty(next.Combat.Get(next.Echo).Effects);
    }

    [Fact]
    public void DeadEchoRequiresRestorationAndCannotBeCreatedAsALiveBody()
    {
        var id = Guid.NewGuid(); var f = new Fight(id);
        Assert.True(f.Combat.PayHealth(f.Echo, f.Combat.Get(f.Echo).Health, true));
        var saved = f.Capture(); Assert.Equal(SavedEchoAvailability.RequiresRestoration, saved.Availability); Assert.Null(saved.ActiveSlot);
        var next = new Fight(id);
        Assert.Throws<InvalidDataException>(() => CompanionRuntimeState.Restore(next.Combat, next.Abilities, next.Echo, saved, [], 100));
        Assert.Equal(saved.DeathId, EchoCombatDocuments.Deserialize(EchoCombatDocuments.Serialize([saved])).Single().DeathId);
    }

    [Fact]
    public async Task OwnedStateAndArbitraryEffectRowsRoundTripInTheCharacterCheckpoint()
    {
        var id = Guid.NewGuid(); var f = new Fight(id); f.Combat.ApplyEffect(default, f.Echo, "echo_test_dot");
        var saved = f.Capture(); var effects = CompanionRuntimeState.CaptureEffects(f.Combat, f.Echo);
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        var initial = CharacterPersistenceTests.World().CreateInitialCharacter();
        var session = await store.OpenAsync("", initial, default); var token = session.IssuedToken;
        await store.SaveAsync([new(session, initial with {OwnedEchoes = [saved], Effects = effects, Mana = 40})], default);
        await session.DisposeAsync();
        await using var loaded = await store.OpenAsync(token, initial, default);
        Assert.Equal(40, loaded.State.Mana); var row = Assert.Single(loaded.State.OwnedEchoes!);
        Assert.Equal(saved.InstanceId, row.InstanceId); Assert.Equal(id, Assert.Single(loaded.State.Effects!).TargetEcho);
        var next = new Fight(id); CompanionRuntimeState.Restore(next.Combat, next.Abilities, next.Echo, row, loaded.State.Effects!, 0);
        Assert.Single(next.Combat.Get(next.Echo).Effects);
    }

    [Fact]
    public void DocumentsRejectWrongBodyTargetsDuplicateSlotsAndCorruptLife()
    {
        var f = new Fight(Guid.NewGuid()); var saved = f.Capture();
        Assert.Throws<InvalidDataException>(() => EchoCombatDocuments.Serialize([saved, saved with {InstanceId = Guid.NewGuid()}]));
        Assert.Throws<InvalidDataException>(() => EchoCombatDocuments.Serialize([saved with {Health = 0}]));
        Assert.Throws<InvalidDataException>(() => EchoCombatDocuments.Serialize([saved with {ActiveSlot = null}]));
        var row = EchoCombatDocuments.Serialize([saved]).Single();
        Assert.Throws<InvalidDataException>(() => EchoCombatDocuments.Deserialize([row with {InstanceId = Guid.NewGuid()}]));
        Assert.Throws<InvalidDataException>(() => EchoCombatDocuments.Deserialize([row with {State = row.State.Replace("\"Version\":1", "\"Version\":2")}]));
        f.Combat.ApplyEffect(default, f.Echo, "echo_test_dot"); var effects = CompanionRuntimeState.CaptureEffects(f.Combat, f.Echo);
        var next = new Fight(Guid.NewGuid());
        Assert.Throws<InvalidDataException>(() => CompanionRuntimeState.Restore(next.Combat, next.Abilities, next.Echo, saved, effects, 0));
        Assert.Throws<InvalidDataException>(() => EchoCombatDocuments.ValidateEffectTargets([], effects));
    }

    [Fact]
    public void DisabledEchoFeaturePreservesOwnedStateAndDoesNotApplyEchoEffectsToOwner()
    {
        var f=new Fight(Guid.NewGuid());f.Combat.ApplyEffect(default,f.Echo,"echo_test_dot");
        var owned=f.Capture(null);var effects=CompanionRuntimeState.CaptureEffects(f.Combat,f.Echo);
        var world=CharacterPersistenceTests.World();var initial=world.CreateInitialCharacter() with {OwnedEchoes=[owned],Effects=effects};
        var player=world.AddPlayer(42,new(1),initial);
        Assert.Empty(world.Combat!.Get(player.EntityId).Effects);
        world.Simulate(.5f);var saved=world.CaptureCharacter(42);
        Assert.Equal(owned.SavedAtUnixMilliseconds,Assert.Single(saved.OwnedEchoes!).SavedAtUnixMilliseconds);
        Assert.Equal(effects, saved.Effects!);
    }

    [Fact]
    public void RegionalCargoFreezesOwnedCooldownArraysAndEffectTargetsWithoutAliasing()
    {
        var f=new Fight(Guid.NewGuid());f.Combat.ApplyEffect(default,f.Echo,"echo_test_dot");
        var owned=f.Capture();var effects=CompanionRuntimeState.CaptureEffects(f.Combat,f.Echo);
        var state=CharacterPersistenceTests.World().CreateInitialCharacter() with {OwnedEchoes=[owned],Effects=effects};
        var cargo=FrozenCharacterState.Capture(state);owned.Cooldowns[0]=new("corrupted",0);
        var restored=cargo.Restore();Assert.NotEqual("corrupted",Assert.Single(restored.OwnedEchoes!).Cooldowns[0].AbilityId);
        Assert.Equal(owned.InstanceId,Assert.Single(restored.Effects!).TargetEcho);
        restored.OwnedEchoes![0].Cooldowns[0]=new("changed_again",0);
        Assert.NotEqual("changed_again",cargo.Restore().OwnedEchoes![0].Cooldowns[0].AbilityId);
    }

    [Fact]
    public async Task CheckpointCacheComparesDurableValuesRatherThanMutableRosterReferences()
    {
        var f=new Fight(Guid.NewGuid());var owned=f.Capture(null);
        var state=CharacterPersistenceTests.World().CreateInitialCharacter() with {OwnedEchoes=[owned]};
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var session=await store.OpenAsync("",state,default);var token=session.IssuedToken;
        await store.SaveAsync([new(session,state)],default);session.Revision++;
        owned.Cooldowns[0]=owned.Cooldowns[0] with {Seconds=1,AvailableCharges=0,RechargeDuration=2};
        await store.SaveAsync([new(session,state)],default);await session.DisposeAsync();
        await using var loaded=await store.OpenAsync(token,state,default);
        Assert.Equal(1,Assert.Single(loaded.State.OwnedEchoes!).Cooldowns[0].Seconds);
    }
}

