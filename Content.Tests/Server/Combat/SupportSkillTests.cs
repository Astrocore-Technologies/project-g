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

public sealed class SupportSkillTests
{
    internal static JsonNode Document(string operation="Heal",bool allowOthers=false)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        var skill=data["abilities"]![1]!;
        skill["contactDisposition"]="Support";skill["power"]=0;skill["magicAttackScale"]=0;
        skill["range"]=8;skill["radius"]=2;skill["manaCost"]=8;skill["cooldownSeconds"]=2;
        skill["action"]!["preparation"]!["seconds"]=.25;skill["action"]!["preparation"]!["speed"]="None";
        skill["effects"]=new JsonArray(new JsonObject
        {
            ["operation"]=operation,["trigger"]="Contact",["target"]="HitTarget",
            ["power"]=new JsonObject{["flat"]=10},["allowOtherPlayers"]=allowOthers
        });
        data["creatures"]![0]!["baseHealthRecovery"]=0;data["balance"]!["healthRecovery"]!["vitality"]=0;
        data["creatures"]![0]!["baseManaRecovery"]=0;data["balance"]!["manaRecovery"]!["wisdom"]=0;
        return data;
    }
    internal static ContentCatalog Catalog(string operation="Heal",bool allowOthers=false)=>Parse(Document(operation,allowOthers));
    private static ContentCatalog Parse(JsonNode data)=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    private static ServerWorld World(ContentCatalog catalog,bool critical=false,NavigationOptions? navigation=null)=>new(Options.Create(new MovementOptions()),
        Options.Create(new InterestOptions()),navigation is null?null:Options.Create(navigation),catalog:catalog,
        hitRoll:()=>throw new InvalidOperationException("Support must not roll evasion."),criticalRoll:()=>critical?0:1);
    private static ServerPlayer Add(ServerWorld world,int connection,ulong player,float x)=>world.AddPlayer(connection,new(player),
        world.CreateInitialCharacter() with {X=x,Z=0,Health=20,Mana=7});
    private static List<CombatEffectEvent> Finish(ServerWorld world)
    {
        var events=new List<CombatEffectEvent>();
        for(var i=0;i<12;i++){world.Simulate(.05f);events.AddRange(world.Combat!.EffectEvents);Assert.Empty(world.Abilities!.Hits);}
        return events;
    }
    private static void Cast(ServerWorld world,ServerPlayer source,uint sequence=1,Vector2? aim=null)
    {
        // The caster starts with seven MP; refill only the caster for the eight-MP ability.
        if(sequence==1)world.Combat!.RestoreMana!(source.EntityId,100);
        Assert.True(world.TryQueueAbility(source.ConnectionId,new(sequence,world.Tick,2,aim??new(-6,0)),0));world.Simulate(.05f);
        Assert.Equal(AbilityOutcome.Accepted,world.Abilities!.Results[source.EntityId].Outcome);
    }

    [Fact]public void AuthoredAreaHealsOnlySelfAndPartyWithCritAndNoOffensiveHitRoll()
    {
        var world=World(Catalog(),true);var source=Add(world,42,1,-7);var party=Add(world,43,2,-6);var other=Add(world,44,3,-5);
        var outside=Add(world,45,4,-2);var combat=world.Combat!;combat.SupportPermission=(s,t)=>t==party.EntityId||t==outside.EntityId;
        var actor=combat.Get(source.EntityId);combat.ApplyEquipment(source.EntityId,(actor.Stats with {CriticalChance=1,CriticalDamage=2},actor.Weapon,actor.AttackInterval));
        Cast(world,source);Assert.Equal(20,combat.Get(party.EntityId).Health);
        var events=Finish(world);Assert.Equal(40,combat.Get(party.EntityId).Health);Assert.Equal(40,combat.Get(source.EntityId).Health);
        Assert.Equal(20,combat.Get(other.EntityId).Health);Assert.Equal(20,combat.Get(outside.EntityId).Health);
        Assert.Equal(2,events.Count);Assert.All(events,e=>{Assert.Equal(CombatEffectEventKind.Healing,e.Kind);Assert.True(e.Critical);Assert.True(e.Execution>0);});
        Assert.False(world.TryQueueAbility(42,new(1,world.Tick,2,new(-6,0)),0));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void MembershipIsRecheckedAtImpactAndOutsideGroupPermissionBelongsToEachPayload(bool allow)
    {
        var world=World(Catalog(allowOthers:allow));var source=Add(world,42,1,-7);var target=Add(world,43,2,-6);
        var member=true;world.Combat!.SupportPermission=(_,_)=>member;Cast(world,source);member=false;Finish(world);
        Assert.Equal(allow?30:20,world.Combat.Get(target.EntityId).Health);
    }

    [Fact]public void ResurrectionOperationUsesTheBodyAndPreservesResourcesCooldownsAndPosition()
    {
        var world=World(Catalog("Resurrect"));long now=100000;world.PvpClock=()=>now;
        var source=Add(world,42,1,-7);var target=Add(world,43,2,-6);var combat=world.Combat!;
        combat.SupportPermission=(_,_)=>true;combat.PayHealth(target.EntityId,20,true);combat.RestoreDefense(target.EntityId,new(1,13,.5,1));
        var cooldowns=world.Abilities!.CaptureCooldowns(target.EntityId);var position=target.Position;
        Cast(world,source);var events=new List<CombatEffectEvent>();
        for(var i=0;i<12&&combat.Get(target.EntityId).Health==0;i++)
        {world.Simulate(.05f);events.AddRange(combat.EffectEvents);}
        Assert.Equal(10,combat.Get(target.EntityId).Health);Assert.False(combat.CaptureLife(target.EntityId).Dead);
        Assert.Equal(7,world.Abilities.Mana(target.EntityId));Assert.Equal(13,combat.DefenseState(target.EntityId,world.Tick).Stamina);
        Assert.Equal(cooldowns,world.Abilities.CaptureCooldowns(target.EntityId));Assert.Equal(position,target.Position);
        Assert.Equal(CombatEffectEventKind.Resurrection,Assert.Single(events).Kind);
        combat.PayHealth(target.EntityId,10,true);now+=60000;
        // A fresh skill is ready, but an expired body is no longer a resurrection target.
        world.Abilities.ResetCooldown(source.EntityId,2);Cast(world,source,2);Assert.Empty(Finish(world));Assert.Equal(0,combat.Get(target.EntityId).Health);
    }

    [Fact]public void SupportAreaUsesRangeHeightAndLineOfSightBeforeApplyingPayloads()
    {
        var world=World(Catalog(allowOthers:true),navigation:new(){BlockedAreas=[new(){X=10,Z=14,Width=1,Height=2}]});
        var source=Add(world,42,1,-7);var target=Add(world,43,2,-6);var occluded=Add(world,44,3,-3.5f);
        // Different floors do not share an area merely because their X/Z coordinates overlap.
        world.Combat!.Move(target.EntityId,target.Position,4);Cast(world,source,aim:new(-5.5f,0));Finish(world);
        Assert.Equal(20,world.Combat.Get(target.EntityId).Health);Assert.Equal(20,world.Combat.Get(occluded.EntityId).Health);
        world.Abilities!.ResetCooldown(source.EntityId,2);var mana=world.Abilities.Mana(source.EntityId);
        Assert.True(world.TryQueueAbility(42,new(2,world.Tick,2,new(-3,0)),0));world.Simulate(.05f);
        Assert.Equal(AbilityOutcome.InvalidAim,world.Abilities.Results[source.EntityId].Outcome);
        Assert.Equal(mana,world.Abilities.Mana(source.EntityId));
        Assert.True(world.TryQueueAbility(42,new(3,world.Tick,2,new(5,0)),0));world.Simulate(.05f);
        Assert.Equal(AbilityOutcome.InvalidAim,world.Abilities.Results[source.EntityId].Outcome);Assert.Equal(mana,world.Abilities.Mana(source.EntityId));
    }

    [Theory]
    [InlineData("unknownOperation")][InlineData("missingPower")][InlineData("effectAndPower")][InlineData("damageInSupport")]
    [InlineData("zeroPower")][InlineData("unsupportedSupportShape")][InlineData("resurrectSelf")]
    public void InvalidAuthoredSupportFailsAtContentLoad(string scenario)
    {
        var data=Document();var skill=data["abilities"]![1]!;var payload=skill["effects"]![0]!;
        switch(scenario)
        {
            case "unknownOperation":payload["operation"]="Unknown";break;
            case "missingPower":payload.AsObject().Remove("power");break;
            case "effectAndPower":payload["effectId"]="sword_bleed";break;
            case "damageInSupport":skill["power"]=1;break;
            case "zeroPower":payload["power"]!["flat"]=0;break;
            case "unsupportedSupportShape":skill["kind"]="Projectile";break;
            case "resurrectSelf":payload["operation"]="Resurrect";payload["target"]="Self";break;
        }
        var error=Assert.Throws<InvalidDataException>(()=>Parse(data));Assert.Contains("content",error.Message,StringComparison.OrdinalIgnoreCase);
    }
}
