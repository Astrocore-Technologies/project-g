using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class TimedEffectTests
{
    [Fact]
    public void SourcesAreIndependentRefreshKeepsIdentityAndRecalculationNeverAccumulates()
    {
        var world=World(Buff()); var target=world.AddPlayer(42,new(1)); var source=world.AddPlayer(43,new(2));
        var combat=world.Combat!; combat.SupportPermission=(_,_)=>true; var baseline=combat.Get(target.EntityId).Stats;
        Assert.True(combat.ApplyEffect(target.EntityId,target.EntityId,"buff"));
        var instance=combat.Get(target.EntityId).Effects.Values.Single().Id;
        for(var i=0;i<100;i++) Assert.True(combat.ApplyEffect(target.EntityId,target.EntityId,"buff"));
        Assert.Equal(instance,combat.Get(target.EntityId).Effects.Values.Single().Id);
        Assert.Equal(baseline.MeleeAttack+10,combat.Get(target.EntityId).Stats.MeleeAttack);
        Assert.True(combat.ApplyEffect(source.EntityId,target.EntityId,"buff"));
        Assert.Equal(baseline.MeleeAttack+20,combat.Get(target.EntityId).Stats.MeleeAttack);
        var equipment=combat.PrepareEquipment(new(20,10,10,10,10,10,10),[]);
        combat.ApplyEquipment(target.EntityId,equipment);
        Assert.Equal(equipment.Stats.MeleeAttack+20,combat.Get(target.EntityId).Stats.MeleeAttack);
        combat.RemoveEffect(instance);
        Assert.Equal(equipment.Stats.MeleeAttack+10,combat.Get(target.EntityId).Stats.MeleeAttack);
        for(var i=0;i<21;i++) world.Simulate(.05f);
        Assert.Empty(combat.Get(target.EntityId).Effects); Assert.Equal(equipment.Stats,combat.Get(target.EntityId).Stats);
    }

    [Fact]
    public void ExplicitStacksAndPercentGroupsAreAdditiveAndUpdateAbilityCdr()
    {
        var world=World(Buff() with {MaxStacks=3,StackIncrement=1,Modifiers=new() {AttackSpeedMultiplier=.2,CooldownReduction=.2}});
        var target=world.AddPlayer(42,new(1)); var combat=world.Combat!; var baseline=combat.Get(target.EntityId).Stats;
        var cooldown=world.Abilities!.Loadout(target.EntityId,0).Abilities[0].CooldownSeconds;
        for(var i=0;i<5;i++) combat.ApplyEffect(target.EntityId,target.EntityId,"buff");
        Assert.Equal(3,combat.Get(target.EntityId).Effects.Values.Single().Stacks);
        Assert.Equal(baseline.AttackSpeedMultiplier+.6,combat.Get(target.EntityId).Stats.AttackSpeedMultiplier,8);
        Assert.Equal(.6,combat.Get(target.EntityId).Stats.CooldownReduction,8);
        Assert.Equal(cooldown*.4,world.Abilities.Loadout(target.EntityId,0).Abilities[0].CooldownSeconds,8);
    }

    [Fact]
    public void EnvironmentImmunityAndTaggedCleanseDispelFollowDefinitions()
    {
        var buff=Buff() with {RequiredEnvironmentTags=["rain"],Dispellable=true,Tags=["weather"]};
        var debuff=Buff() with {Id="debuff",Disposition=EffectDisposition.Harmful,Cleansable=true,Tags=["poison"]};
        var immunity=Buff() with {Id="immunity",ImmuneTags=["poison"],Modifiers=new()};
        var world=World(buff,debuff,immunity); var target=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        var baseline=combat.Get(target.EntityId).Stats.MeleeAttack;
        combat.ApplyEffect(target.EntityId,target.EntityId,"buff"); Assert.Equal(baseline,combat.Get(target.EntityId).Stats.MeleeAttack);
        combat.SetEnvironmentTags(target.EntityId,new HashSet<string>{"rain"}); Assert.Equal(baseline+10,combat.Get(target.EntityId).Stats.MeleeAttack);
        Assert.True(combat.ApplyEffect(default,target.EntityId,"debuff"));
        Assert.Equal(1,combat.RemoveEffects(target.EntityId,cleanse:true,new HashSet<string>{"poison"}));
        combat.ApplyEffect(target.EntityId,target.EntityId,"immunity");
        Assert.False(combat.ApplyEffect(default,target.EntityId,"debuff"));
        Assert.Equal(0,combat.RemoveEffects(target.EntityId,cleanse:false,new HashSet<string>{"poison"}));
        Assert.Equal(1,combat.RemoveEffects(target.EntityId,cleanse:false,new HashSet<string>{"weather"}));
        Assert.Equal(baseline,combat.Get(target.EntityId).Stats.MeleeAttack);
    }

    [Fact]
    public void ChangingMaximumAndOfflineExpiryPreserveResourceFractions()
    {
        var world=World(Buff() with {Modifiers=new() {MaxHealth=100,MaxMana=100},DurationSeconds=2});
        var target=world.AddPlayer(42,new(1)); var combat=world.Combat!; var baseline=combat.Get(target.EntityId).Stats;
        combat.Get(target.EntityId).Health=7;
        combat.ApplyEffect(target.EntityId,target.EntityId,"buff");
        Assert.Equal(7/baseline.MaxHealth*(baseline.MaxHealth+100),combat.Get(target.EntityId).Health);
        Assert.Equal(baseline.MaxMana+100,world.Abilities!.Loadout(target.EntityId,0).MaxMana);
        var state=world.CaptureCharacter(42) with {Health=baseline.MaxHealth+70,Mana=baseline.MaxMana+70,SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds()};
        var next=World(Buff() with {Modifiers=new() {MaxHealth=100,MaxMana=100},DurationSeconds=2});
        var restored=next.AddPlayer(42,new(1),state);
        Assert.Equal(state.Health/(baseline.MaxHealth+100)*baseline.MaxHealth,next.Combat!.Get(restored.EntityId).Health);
        Assert.Equal(state.Mana/(baseline.MaxMana+100)*baseline.MaxMana,next.Abilities!.Mana(restored.EntityId));
        Assert.Empty(next.Combat.Get(restored.EntityId).Effects);
    }

    [Fact]
    public void OnlyExplicitlyMarkedEffectsSurviveDeathAndLogout()
    {
        var world=World(Buff(),Buff() with {Id="persistent",PersistThroughDeath=true},Buff() with {Id="session",PersistThroughLogout=false});
        var target=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        foreach(var id in new[]{"buff","persistent","session"}) combat.ApplyEffect(target.EntityId,target.EntityId,id);
        var saved=world.CaptureCharacter(42); Assert.Equal(3,saved.Effects!.Count);
        var next=World(Buff(),Buff() with {Id="persistent",PersistThroughDeath=true},Buff() with {Id="session",PersistThroughLogout=false});
        var restored=next.AddPlayer(42,new(1),saved);
        Assert.Equal(2,next.Combat!.Get(restored.EntityId).Effects.Count);
        combat.DamagePermission=(_,_)=>true; combat.ApplyAbilityDamage(target.EntityId,100000,world.TrainingTargetId);
        Assert.Single(combat.Get(target.EntityId).Effects);
        Assert.Equal("persistent",combat.Get(target.EntityId).Effects.Values.Single().Definition.Id);
    }

    [Fact]
    public async Task TimedModifiersSurviveRealDatabaseAndDoNotDuplicateEquipmentBonus()
    {
        var world=World(Buff()); var target=world.AddPlayer(42,new(1)); var combat=world.Combat!;
        combat.ApplyEffect(target.EntityId,target.EntityId,"buff"); var expected=combat.Get(target.EntityId).Stats;
        var store=new Content.Tests.Server.Persistence.SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var session=await store.OpenAsync("",world.CreateInitialCharacter(),CancellationToken.None); var identity=session.IssuedToken;
        await store.SaveAsync([new(session,world.CaptureCharacter(42))],CancellationToken.None); await session.DisposeAsync();
        await using var saved=await store.OpenAsync(identity,world.CreateInitialCharacter(),CancellationToken.None);
        var next=World(Buff()); var restored=next.AddPlayer(42,new(1),saved.State);
        Assert.Equal(expected,next.Combat!.Get(restored.EntityId).Stats);
        Assert.Single(next.Combat.Get(restored.EntityId).Effects);
    }

    [Fact]
    public void HundredsOfDifferentBuffsHaveNoGlobalGameplayCap()
    {
        var definitions=Enumerable.Range(0,150).Select(i=>Buff() with {Id="buff_"+i}).ToArray();
        var world=World(definitions); var target=world.AddPlayer(42,new(1));
        var initial=world.Combat!.Get(target.EntityId).Stats.MeleeAttack;
        foreach(var definition in definitions) Assert.True(world.Combat.ApplyEffect(target.EntityId,target.EntityId,definition.Id));
        Assert.Equal(150,world.Combat.Get(target.EntityId).Effects.Count);
        Assert.Equal(initial+1500,world.Combat.Get(target.EntityId).Stats.MeleeAttack);
        Assert.Equal(150,world.CaptureCharacter(42).Effects!.Count);
    }

    [Fact]
    public void RegionCargoFreezesEffectDocumentsAndPreservesSessionEffectsDuringTravel()
    {
        var definition=Buff() with {PersistThroughLogout=false}; var world=World(definition); var player=world.AddPlayer(42,new(1));
        world.Combat!.ApplyEffect(player.EntityId,player.EntityId,"buff");
        var original=world.CaptureCharacter(42); var effects=original.Effects!.ToArray();
        var cargo=Content.Server.Regions.FrozenCharacterState.Capture(original with {Effects=effects});
        effects[0]=effects[0] with {RemainingSeconds=.001};
        var restored=cargo.Restore();
        Assert.Equal(1,restored.Effects![0].RemainingSeconds);
        var destination=World(definition); var traveller=destination.AddPlayer(42,new(1),restored,restoringFromLogout:false);
        Assert.Single(destination.Combat!.Get(traveller.EntityId).Effects);
    }

    [Fact]
    public void EffectCdrPreservesAlreadyRunningCooldownAndRemovalRestoresFutureIntervals()
    {
        var world=World(Buff() with {DurationSeconds=5,Modifiers=new(){CooldownReduction=.5}});
        var player=world.AddPlayer(42,new(1));
        world.TryQueueAbility(42,new(1,0,1,Vector2.UnitX),0);
        for(var i=0;i<15;i++) world.Simulate(.05f);
        var before=world.Abilities!.Loadout(player.EntityId,world.Tick).Abilities.Single(a=>a.Id==1);
        Assert.True(before.RechargeInSeconds>0);
        world.Combat!.ApplyEffect(player.EntityId,player.EntityId,"buff");
        var after=world.Abilities.Loadout(player.EntityId,world.Tick).Abilities.Single(a=>a.Id==1);
        Assert.Equal(before.RechargeInSeconds,after.RechargeInSeconds);
        Assert.Equal(before.RechargeDuration,after.RechargeDuration);
        Assert.Equal(before.CooldownSeconds*.5,after.CooldownSeconds);
        world.Combat.RemoveEffect(world.Combat.Get(player.EntityId).Effects.Values.Single().Id);
        var removed=world.Abilities.Loadout(player.EntityId,world.Tick).Abilities.Single(a=>a.Id==1);
        Assert.Equal(before.RechargeInSeconds,removed.RechargeInSeconds);
        Assert.Equal(before.CooldownSeconds,removed.CooldownSeconds);
    }

    [Fact]
    public void MalformedDefinitionsFailBeforeActorsStart()
    {
        Assert.Throws<InvalidDataException>(()=>World(Buff() with {DurationSeconds=0}));
        Assert.Throws<InvalidDataException>(()=>World(Buff() with {MaxStacks=0}));
        Assert.Throws<InvalidDataException>(()=>World(Buff() with {Disposition=EffectDisposition.Harmful,Dispellable=true}));
        Assert.Throws<InvalidDataException>(()=>World(Buff() with {RequiredEnvironmentTags=["rain"],ExcludedEnvironmentTags=["rain"]}));
        Assert.Throws<InvalidDataException>(()=>World(Buff() with {Tags=["poison","poison"]}));
    }

    internal static EffectDefinition Buff()=>new(){Id="buff",DurationSeconds=1,Modifiers=new(){MeleeAttack=10}};
    internal static ServerWorld World(params EffectDefinition[] effects)
    {
        var json=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!.AsObject();
        foreach(var effect in effects) json["effects"]!.AsArray().Add(JsonSerializer.SerializeToNode(effect,new JsonSerializerOptions
        {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter()}}));
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
        return new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>1);
    }
}
