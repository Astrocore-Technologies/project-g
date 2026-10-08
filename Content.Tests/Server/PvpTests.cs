using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class PvpTests
{
    internal static ServerWorld World(ContentCatalog? catalog=null)=>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),Options.Create(new NavigationOptions{BlockedAreas=[new(){X=14,Z=10,Width=2,Height=10}]}),catalog??ContentCatalogTests.Load(),inventory:Options.Create(new InventoryOptions{Enabled=true}),groundItems:Options.Create(new GroundItemOptions{Enabled=true,Seeds=[]}),npc:Options.Create(new NpcOptions{Enabled=true,X=13,Z=13}),worldStory:Options.Create(new WorldStoryOptions{Enabled=true}),crafting:Options.Create(new CraftingOptions{Enabled=true}),echoes:Options.Create(new EchoOptions{Enabled=true}));
    internal static CharacterState Initial(ServerWorld w,float x=-5,float z=-5,bool bound=false,bool equipped=true)
    {
        var s=w.CreateInitialCharacter();return s with {X=x,Z=z,Health=20,Progression=s.Progression! with {Experience=20},Inventory=new(){Items=[new(Guid.NewGuid(),"crafted_crossing_blade",equipped?EquipmentSlot.Weapon:EquipmentSlot.None){Bound=bound,Enhancement=2,Condition=new(){Current=60,Maximum=70,Revision=7}}]}};
    }
    internal static PvpOutcome Command(ServerWorld w,int c,ulong seq,PvpMode mode,PvpAction action=PvpAction.Mode,ulong death=0)
    {w.ClearPvpResults();Assert.True(w.TryQueuePvp(c,new(seq,action,mode,mode==PvpMode.Criminal,death)));w.Simulate(.05f);return w.PvpResults[w.GetPlayer(c).EntityId].Outcome;}
    internal static void Kill(ServerWorld w,NetworkEntityId killer,NetworkEntityId victim)
    {Assert.True(w.Combat!.ApplyAbilityDamage(victim,100000,killer)>0);w.Simulate(.05f);}
    internal static ulong LootHandle(ServerWorld w,Vector2 at){var ids=new HashSet<NetworkEntityId>();w.GroundItems!.Query(at,2,ids);return Assert.Single(ids).Value;}
    [Fact]public void PeacefulSafeZoneAndTagSwitchChecksAreServerAuthoritative()
    {
        var w=World();long now=100000;w.PvpClock=()=>now;var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,-4.5f));
        Assert.Equal(0,w.Combat!.ApplyAbilityDamage(b.EntityId,1,a.EntityId));Assert.Equal(PvpOutcome.Accepted,Command(w,43,1,PvpMode.Voluntary));Assert.True(w.PublicPvp(b.EntityId).Tagged);Assert.True(w.Combat.ApplyAbilityDamage(b.EntityId,1,a.EntityId)>0);
        Assert.Equal(PvpOutcome.NotReady,Command(w,43,2,PvpMode.Peaceful));now+=120001;Assert.Equal(PvpOutcome.Accepted,Command(w,43,3,PvpMode.Peaceful));
        var safe=w.AddPlayer(44,new(3),Initial(w,-9,0));Assert.Equal(PvpOutcome.Protected,Command(w,44,1,PvpMode.Criminal));Assert.Equal(0,w.Combat.ApplyAbilityDamage(safe.EntityId,100000,a.EntityId));
        Command(w,42,1,PvpMode.Criminal);StarterZoneTests.Walk(w,42,new(-9,0));Assert.True(w.PublicPvp(a.EntityId).Tagged);Assert.Equal(0,w.Combat.ApplyAbilityDamage(b.EntityId,1,a.EntityId));
    }
    [Fact]public void CriminalDamageDeathDropAndRespawnAreExactlyOnce()
    {
        var w=World();long now=100000;w.PvpClock=()=>now;var a=w.AddPlayer(42,new(1),Initial(w));var before=Initial(w,-4.5f);var b=w.AddPlayer(43,new(2),before);var calls=0;w.DeathRoll=n=>{calls++;return 0;};
        Assert.Equal(PvpOutcome.Accepted,Command(w,42,1,PvpMode.Criminal));Assert.True(w.Combat!.ApplyAbilityDamage(b.EntityId,1,a.EntityId)>0);Assert.True(w.PublicPvp(a.EntityId).Aggressor);Assert.Equal(-10,w.PrivatePvp(a.EntityId).Reputation);
        Assert.True(w.Combat.ApplyAbilityDamage(a.EntityId,1,b.EntityId)>0);Assert.Equal(0,w.PrivatePvp(b.EntityId).Pk);Kill(w,a.EntityId,b.EntityId);
        var death=w.PrivatePvp(b.EntityId);Assert.True(death.Dead);Assert.True(death.PvpDeath);Assert.Equal(2,death.ExperienceLost);Assert.Equal(-110,w.PrivatePvp(a.EntityId).Reputation);Assert.Equal(1,w.PrivatePvp(a.EntityId).Pk);Assert.Equal(1,calls);Assert.Empty(w.CaptureCharacter(43).Inventory!.Items);
        var loot=Assert.Single(w.CaptureWorldNode().DeathLoot!);Assert.Equal(before.Inventory!.Items[0] with {EquippedSlot=EquipmentSlot.None},loot.Item);
        Assert.Equal(PvpOutcome.NotReady,Command(w,43,1,PvpMode.Peaceful,PvpAction.Respawn,death.DeathId));now+=60000;Assert.Equal(PvpOutcome.Accepted,Command(w,43,2,PvpMode.Peaceful,PvpAction.Respawn,death.DeathId));Assert.False(w.PrivatePvp(b.EntityId).Dead);Assert.Equal(new Vector2(-9,0),b.Position);Assert.Equal(w.Combat.Get(b.EntityId).Stats.MaxHealth,w.Combat.Get(b.EntityId).Health);
        Assert.Equal(PvpOutcome.AlreadyProcessed,Command(w,43,2,PvpMode.Peaceful,PvpAction.Respawn,death.DeathId));Assert.Equal(1,calls);Assert.Equal(18,w.CaptureCharacter(43).Progression!.Experience);
    }
    [Fact]public void PvpPickupRequiresTagFiveSecondsAndRaceHasExactlyOneOwner()
    {
        var w=World();long now=100000;w.PvpClock=()=>now;var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,-4.5f));var c=w.AddPlayer(44,new(3),Initial(w,-4.8f));Command(w,42,1,PvpMode.Criminal);Kill(w,a.EntityId,b.EntityId);var drop=Assert.Single(w.CaptureWorldNode().DeathLoot!).Item;var h=LootHandle(w,b.Position);
        Assert.True(w.TryQueuePickup(44,new(1,h)));w.Simulate(.05f);Assert.Equal(PickupOutcome.InvalidState,w.GroundItems!.Results[c.EntityId].Outcome);Assert.Single(w.CaptureWorldNode().DeathLoot!);
        Command(w,44,1,PvpMode.Voluntary);Assert.True(w.TryQueuePickup(42,new(1,h)));Assert.True(w.TryQueuePickup(44,new(2,h)));w.Simulate(.05f);Assert.Equal(PickupOutcome.Channeling,w.GroundItems.Results[a.EntityId].Outcome);Assert.Equal(PickupOutcome.Channeling,w.GroundItems.Results[c.EntityId].Outcome);
        now+=4999;w.Simulate(.05f);Assert.Single(w.CaptureWorldNode().DeathLoot!);now++;w.Simulate(.05f);Assert.Empty(w.CaptureWorldNode().DeathLoot!);var results=new[]{w.GroundItems.Results[a.EntityId].Outcome,w.GroundItems.Results[c.EntityId].Outcome};Assert.Contains(PickupOutcome.Accepted,results);Assert.Contains(PickupOutcome.Missing,results);
        var instances=w.CaptureCharacter(42).Inventory!.Items.Concat(w.CaptureCharacter(44).Inventory!.Items).Where(i=>i.InstanceId==drop.InstanceId);Assert.Equal(drop,Assert.Single(instances));
    }
    [Fact]public void ChannelMovementDamageDisconnectAndTagExpiryInterruptWithoutLoss()
    {
        var w=World();long now=100000;w.PvpClock=()=>now;var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,-4.5f));Command(w,42,1,PvpMode.Criminal);Kill(w,a.EntityId,b.EntityId);var h=LootHandle(w,b.Position);
        w.TryQueuePickup(42,new(1,h));w.Simulate(.05f);Assert.True(w.TryApplyMove(42,new(1,w.Tick,new Vector2(-5,-4))));w.Simulate(.05f);Assert.Equal(PickupOutcome.InvalidState,w.GroundItems!.Results[a.EntityId].Outcome);StarterZoneTests.Walk(w,42,new(-5,-5),2);
        w.TryQueuePickup(42,new(2,h));w.Simulate(.05f);Assert.True(w.Combat!.ApplyAbilityDamage(a.EntityId,1,w.Npc!.Id)>0);w.Simulate(.05f);Assert.Equal(PickupOutcome.Interrupted,w.GroundItems.Results[a.EntityId].Outcome);
        w.TryQueuePickup(42,new(3,h));w.Simulate(.05f);w.DisconnectPvp(42);Assert.Equal(0UL,w.GroundItems.ChannelState(a.EntityId,w.Tick).Handle);
        w.TryQueuePickup(42,new(4,h));w.Simulate(.05f);now+=120001;w.Simulate(.05f);Assert.Equal(PickupOutcome.InvalidState,w.GroundItems.Results[a.EntityId].Outcome);Assert.Single(w.CaptureWorldNode().DeathLoot!);
    }
    [Theory][InlineData(true,true)][InlineData(false,false)]public void BoundAndUnequippedItemsNeverDrop(bool bound,bool equipped)
    {
        var w=World();var a=w.AddPlayer(42,new(1),Initial(w));var seed=Initial(w,-4.5f,bound:bound,equipped:equipped);var b=w.AddPlayer(43,new(2),seed);Command(w,42,1,PvpMode.Criminal);Kill(w,a.EntityId,b.EntityId);Assert.Empty(w.CaptureWorldNode().DeathLoot??[]);Assert.Equal(seed.Inventory!.Items[0],Assert.Single(w.CaptureCharacter(43).Inventory!.Items));
    }
    [Fact]public void UnboundLegendaryDropsAndExpiryNeverReturnsOrRecreatesIt()
    {
        var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;foreach(var i in json["items"]!.AsArray())if(i!["id"]!.GetValue<string>()=="crafted_crossing_blade")i["legendary"]=true;
        var w=World(ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));long now=100000;w.PvpClock=()=>now;var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,-4.5f));Command(w,42,1,PvpMode.Criminal);Kill(w,a.EntityId,b.EntityId);var saved=w.CaptureWorldNode();var dead=w.CaptureCharacter(43);Assert.Single(saved.DeathLoot!);
        var restart=World();restart.PvpClock=()=>now;restart.RestoreWorldNode(SavedWorldNode.Deserialize(saved.Serialize()),1);restart.AddPlayer(43,new(2),dead);now+=1800000;restart.Simulate(.05f);Assert.Empty(restart.CaptureWorldNode().DeathLoot!);Assert.Empty(restart.CaptureCharacter(43).Inventory!.Items);Assert.Equal(dead.Progression!.Pvp!.DeathId,restart.PrivatePvp(restart.GetPlayer(43).EntityId).DeathId);
    }
    [Fact]public void PveAndMixedDeathUseCorrectLossAndLegacyDeadSaveIsNotPenalizedAgain()
    {
        var w=World();long now=100000;w.PvpClock=()=>now;var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,-4.5f));Kill(w,w.Npc!.Id,b.EntityId);Assert.False(w.PrivatePvp(b.EntityId).PvpDeath);Assert.Equal(1,w.PrivatePvp(b.EntityId).ExperienceLost);Assert.Single(w.CaptureCharacter(43).Inventory!.Items);
        var c=w.AddPlayer(44,new(3),Initial(w,-4.8f));Command(w,42,1,PvpMode.Criminal);w.Combat!.ApplyAbilityDamage(c.EntityId,1,a.EntityId);Kill(w,w.Npc.Id,c.EntityId);Assert.True(w.PrivatePvp(c.EntityId).PvpDeath);Assert.Equal(1,w.PrivatePvp(a.EntityId).Pk);
        var d=w.AddPlayer(45,new(4),Initial(w,-6) with {Health=0});Assert.True(w.PrivatePvp(d.EntityId).Dead);Assert.Equal(20,w.CaptureCharacter(45).Progression!.Experience);Assert.Single(w.CaptureCharacter(45).Inventory!.Items);
    }

    [Fact]public void OtherInteractionsCancelPickupBeforeItCanComplete()
    {
        var w=World();long now=100000;w.PvpClock=()=>now;var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,-4.5f));Command(w,42,1,PvpMode.Criminal);Kill(w,a.EntityId,b.EntityId);var h=LootHandle(w,b.Position);
        w.TryQueuePickup(42,new(1,h));w.Simulate(.05f);Assert.NotEqual(0UL,w.GroundItems!.ChannelState(a.EntityId,w.Tick).Handle);
        Assert.True(w.TryQueueCraft(42,new(1,CraftAction.Gather,1)));Assert.Equal(0UL,w.GroundItems.ChannelState(a.EntityId,w.Tick).Handle);now+=5000;w.Simulate(.05f);Assert.Single(w.CaptureWorldNode().DeathLoot!);
    }
    [Fact]public void FullDeathLootBudgetPreservesEquipmentAndStillAppliesDeathPenalty()
    {
        var w=World();long now=100000;w.PvpClock=()=>now;var item=Initial(w).Inventory!.Items[0] with {EquippedSlot=EquipmentSlot.None};
        w.RestoreWorldNode(new(){DeathLoot=Enumerable.Range(0,8).Select(i=>new SavedDeathLoot(item with {InstanceId=Guid.NewGuid()},-6,-5,now+1800000)).ToArray()},1);
        var a=w.AddPlayer(42,new(1),Initial(w));var before=Initial(w,-4.5f);var b=w.AddPlayer(43,new(2),before);Command(w,42,1,PvpMode.Criminal);Kill(w,a.EntityId,b.EntityId);
        Assert.True(w.PrivatePvp(b.EntityId).Dead);Assert.True(w.PrivatePvp(b.EntityId).DropSkipped);Assert.Equal(2,w.PrivatePvp(b.EntityId).ExperienceLost);Assert.Equal(1,w.PrivatePvp(a.EntityId).Pk);Assert.Equal(before.Inventory!.Items, w.CaptureCharacter(43).Inventory!.Items);Assert.Equal(8,w.CaptureWorldNode().DeathLoot!.Length);
    }
    [Fact]public void EchoDamageUsesOwnerAndCannotOriginateInsideCity()
    {
        var w=World();var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,-4.5f));Command(w,42,1,PvpMode.Criminal);
        Assert.Equal(0,w.Combat!.ApplyEchoDamage(b.EntityId,1,a.EntityId,new Vector2(-9,0)));Assert.Equal(0,w.PrivatePvp(a.EntityId).Pk);Assert.Equal(0,w.PrivatePvp(a.EntityId).Reputation);
        Assert.True(w.Combat.ApplyEchoDamage(b.EntityId,100000,a.EntityId,a.Position)>0);w.Simulate(.05f);Assert.True(w.PrivatePvp(b.EntityId).PvpDeath);Assert.Equal(1,w.PrivatePvp(a.EntityId).Pk);Assert.Equal(-110,w.PrivatePvp(a.EntityId).Reputation);
    }
    [Fact]public void CorruptSavedPvpAndDeathLootAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>new SavedPvp{Version=2}.Validate());Assert.Throws<InvalidDataException>(()=>new SavedPvp{Dead=true}.Validate());Assert.Throws<InvalidDataException>(()=>new SavedPvp{Episodes=[new(Guid.NewGuid(),1,true),new(Guid.Empty,1,true)]}.Validate());
        var w=World();var item=Initial(w).Inventory!.Items[0] with {EquippedSlot=EquipmentSlot.None};var loot=new SavedDeathLoot(item,-5,-5,100000);
        Assert.Throws<InvalidDataException>(()=>new SavedWorldNode{DeathLoot=[loot,loot]}.Validate());Assert.Throws<InvalidDataException>(()=>(loot with {Item=item with {Bound=true}}).Validate());Assert.Throws<InvalidDataException>(()=>SavedWorldNode.Deserialize("{\"Version\":1,\"Repairs\":0,\"Patrols\":0,\"StormRumor\":false,\"DeathLoot\":null}"));
        Assert.Throws<InvalidDataException>(()=>w.RestoreWorldNode(new(){DeathLoot=[loot with {Item=item with {DefinitionId="missing"}}]},1));
    }

    [Theory][InlineData(1,false)][InlineData(2,false)][InlineData(1,true)][InlineData(2,true)]
    public void RealProjectileAndAreaRespectPvpPolicyAndDoNotFarmExperience(ushort skill,bool criminal)
    {
        var w=World();var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,-5,-4));if(criminal)Command(w,42,1,PvpMode.Criminal);
        var hp=w.Combat!.Get(b.EntityId).Health;var xp=w.CaptureCharacter(42).Progression!.Experience;var hit=false;
        Assert.True(w.TryQueueAbility(42,new(1,w.Tick,skill,skill==1?Vector2.UnitY:b.Position),0));
        for(var n=0;n<80;n++){w.Simulate(.05f);hit|=w.Abilities!.Hits.Any(h=>h.TargetId==b.EntityId);}
        Assert.Equal(criminal,hit);Assert.Equal(criminal,w.Combat.Get(b.EntityId).Health<hp);Assert.Equal(xp,w.CaptureCharacter(42).Progression!.Experience);
    }
}
