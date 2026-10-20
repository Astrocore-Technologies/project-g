using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CommonResourceTests
{
    internal static JsonNode Document()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"resource_capacity","durationSeconds":2,"disposition":"Beneficial","persistThroughLogout":true,"modifiers":{"maxMana":100,"maxStamina":100,"staminaRecovery":5}}"""));
        return data;
    }
    internal static ContentCatalog Catalog(JsonNode? data=null)=>ContentCatalog.Parse(Encoding.UTF8.GetBytes((data??Document()).ToJsonString()));

    private sealed class Fight
    {
        internal readonly ContentCatalog Catalog=CommonResourceTests.Catalog();
        internal readonly CombatSimulation Combat;
        internal readonly NetworkEntityId Id=new(17);
        private uint _tick;
        internal Combatant Actor=>Combat.Get(Id);
        internal Fight(CombatEntityKind kind)
        {
            Combat=new(Catalog,new SpatialIndex(8),new NavigationOptions().CreateGrid(new MovementOptions().ToSettings()),new CombatOptions(),20,8,()=>1,()=>0);
            var definition=kind==CombatEntityKind.Boss?"test_boss":kind==CombatEntityKind.Player?"test_adventurer":"test_creature";
            Combat.Add(Id,Vector2.Zero,kind,definition);
        }
        internal void Step(float seconds)=>Combat.Simulate(seconds,++_tick);
    }

    [Theory][InlineData(CombatEntityKind.Player)][InlineData(CombatEntityKind.Monster)][InlineData(CombatEntityKind.Boss)]
    public void ActorsWithoutAnyAbilitySimulationHaveManaAndStaminaWithFractionalRecovery(CombatEntityKind kind)
    {
        var f=new Fight(kind);Assert.Equal(100,f.Actor.Resources.Stamina);Assert.Equal(f.Actor.Stats.MaxMana,f.Actor.Resources.Mana);
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {ManaRecovery=2.5},f.Actor.Weapon,f.Actor.AttackInterval));
        Assert.Equal(10,f.Combat.DrainMana!(f.Id,10));f.Combat.SpendStamina(f.Id,20);
        f.Step(1);Assert.Equal(80,f.Combat.AvailableStamina(f.Id));Assert.Equal(f.Actor.Stats.MaxMana-7.5,f.Actor.Resources.Mana);
        f.Step(.25f);Assert.Equal(83.75,f.Combat.AvailableStamina(f.Id));
        Assert.Equal(f.Actor.Stats.MaxMana-6.875,f.Actor.Resources.Mana);
        Assert.Equal(6.875,f.Combat.RestoreMana!(f.Id,1000));Assert.Equal(f.Actor.Stats.MaxMana,f.Actor.Resources.Mana);
    }

    [Fact]public void PlayerCapacityChangesPreserveFractionAndZeroCapacityDoesNotCreateFreeDefense()
    {
        var f=new Fight(CombatEntityKind.Player);f.Combat.SpendStamina(f.Id,20);f.Combat.DrainMana!(f.Id,30);
        var stamina=f.Actor.Resources.Stamina;var mana=f.Actor.Resources.Mana;
        Assert.True(f.Combat.ApplyEffect(f.Id,f.Id,"resource_capacity"));
        Assert.Equal(stamina*2,f.Actor.Resources.Stamina);Assert.Equal(mana/140*240,f.Actor.Resources.Mana,10);
        Assert.Equal(200,f.Combat.DefenseState(f.Id,1).MaxStamina);Assert.Equal(20,f.Actor.Stats.StaminaRecovery);
        f.Combat.RestoreStamina(f.Id,200);f.Combat.RestoreMana!(f.Id,500);
        Assert.Equal(200,f.Actor.Resources.Stamina);Assert.Equal(240,f.Actor.Resources.Mana);
        f.Step(2);Assert.Equal(100,f.Actor.Resources.Stamina);Assert.Equal(140,f.Actor.Resources.Mana);
        f.Combat.ApplyEquipment(f.Id,(f.Actor.BaseDerivedStats with {MaxStamina=-1,MaxMana=-1,StaminaRecovery=-5,ManaRecovery=-5},f.Actor.Weapon,f.Actor.AttackInterval));
        Assert.Equal(0,f.Actor.Resources.Stamina);Assert.Equal(0,f.Actor.Resources.Mana);f.Step(10);
        Assert.Equal(0,f.Actor.Resources.Stamina);Assert.Equal(0,f.Actor.Resources.Mana);
        Assert.True(f.Combat.QueueDefense(f.Id,new(1,DefenseAction.Parry,Vector2.UnitY),1));f.Step(.05f);
        Assert.Equal(DefenseOutcome.NoStamina,f.Combat.DefenseState(f.Id,2).Outcome);
        Assert.NotEmpty(NetworkProtocol.Write(f.Combat.DefenseState(f.Id,2)).CopyData());
    }

    [Fact]public void NpcReturnPausesOnlyNaturalHpRecoveryAndHomeResumesWithoutFullHeal()
    {
        var f=new Fight(CombatEntityKind.Monster);
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {HealthRecovery=2,ManaRecovery=3},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Actor.Health=10;f.Combat.RefreshHealthRecovery(f.Id);f.Combat.DrainMana!(f.Id,10);
        f.Step(1);Assert.Equal(12,f.Actor.Health);
        f.Combat.BeginNpcReturn(f.Id);f.Step(2);Assert.Equal(12,f.Actor.Health);
        Assert.Equal(f.Actor.Stats.MaxMana-1,f.Actor.Resources.Mana);
        f.Combat.NpcReturnedHome(f.Id);f.Step(1);Assert.Equal(14,f.Actor.Health);
    }

    [Fact]public void DeadActorsNeverRegenerateAndRevivalResumesDepletedResources()
    {
        var f=new Fight(CombatEntityKind.Monster);f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {ManaRecovery=3},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Combat.SpendStamina(f.Id,60);f.Combat.DrainMana!(f.Id,20);var mana=f.Actor.Resources.Mana;
        f.Actor.Health=0;f.Step(4);Assert.Equal(40,f.Actor.Resources.Stamina);Assert.Equal(mana,f.Actor.Resources.Mana);
        f.Combat.Respawn(f.Id);f.Combat.SpendStamina(f.Id,60);f.Step(2);
        Assert.Equal(55,f.Actor.Resources.Stamina);Assert.Equal(mana+6,f.Actor.Resources.Mana);
        f.Combat.Remove(f.Id);f.Step(5);Assert.False(f.Combat.TryGet(f.Id,out _));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ExpiredOfflineCapacityBuffPreservesSavedFractionWithoutOfflineRecovery(bool legacy)
    {
        var catalog=Catalog();ServerWorld World()=>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog);
        var world=World();var player=world.AddPlayer(42,new(1));var id=player.EntityId;
        Assert.True(world.Combat!.ApplyEffect(id,id,"resource_capacity"));world.Combat.SpendStamina(id,120);world.Combat.DrainMana!(id,144);
        var saved=world.CaptureCharacter(42) with {SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds()};
        Assert.Equal(80,saved.Defense!.Stamina);Assert.Equal(96,saved.Mana);
        if(legacy)saved=saved with {ResourceMaxima=null};
        var next=World();var restored=next.AddPlayer(42,new(1),saved);var actor=next.Combat!.Get(restored.EntityId);
        Assert.Equal(40,actor.Resources.Stamina);Assert.Equal(56,actor.Resources.Mana);Assert.Empty(actor.Effects);
        Assert.Equal(actor.Resources.Mana,next.Abilities!.Mana(restored.EntityId));
    }

    [Fact]public void SavedMaximumContextSurvivesContentCapacityChangesAndJsonRoundTrip()
    {
        var data=Document();
        ServerWorld World(JsonNode document)=>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Catalog(document));
        var world=World(data);var p=world.AddPlayer(42,new(1));var actor=world.Combat!.Get(p.EntityId);
        actor.Health=actor.Stats.MaxHealth*.4;world.Combat.DrainMana!(p.EntityId,actor.Stats.MaxMana*.6);world.Combat.SpendStamina(p.EntityId,60);
        var saved=world.CaptureCharacter(42);
        var json=Content.Server.Persistence.CharacterState.Deserialize(saved.Serialize());Assert.Equal(saved.ResourceMaxima,json.ResourceMaxima);
        data["creatures"]![0]!["modifiers"]!["maxHealth"]=1000;
        data["creatures"]![0]!["modifiers"]!["maxMana"]=1000;
        data["creatures"]![0]!["modifiers"]!["maxStamina"]=1000;
        var next=World(data);var restored=next.AddPlayer(42,new(1),saved);var after=next.Combat!.Get(restored.EntityId);
        Assert.Equal(after.Stats.MaxHealth*.4,after.Health,8);Assert.Equal(after.Stats.MaxMana*.4,after.Resources.Mana,8);
        Assert.Equal(after.Stats.MaxStamina*.4,after.Resources.Stamina,8);
    }

    [Fact]public void OwnerPreviewUsesTheSamePhysicalZeroCapacityAsTheResourceBars()
    {
        var data=Document();data["creatures"]![0]!["modifiers"]!["maxMana"]=-1000;data["creatures"]![0]!["modifiers"]!["maxStamina"]=-1000;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Catalog(data));
        var player=world.AddPlayer(42,new(1));var id=player.EntityId;
        Assert.True(world.TryQueueProgression(42,new(1,ProgressionAction.PreviewStats,0,0,0,new int[7])));world.Simulate(.05f);
        var preview=world.StatPreviews[id];Assert.Equal(0,preview.Current[(int)CharacterStat.MaxStamina]);Assert.Equal(0,preview.Projected[(int)CharacterStat.MaxMana]);
        Assert.Equal(0,world.Combat!.DefenseState(id,world.Tick).MaxStamina);Assert.Equal(0,world.Abilities!.Loadout(id,world.Tick).MaxMana);
    }
}
