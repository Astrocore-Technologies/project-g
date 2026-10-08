using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class CraftingTests
{
    internal static ServerWorld World(ContentCatalog? catalog=null) => new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        Options.Create(new NavigationOptions { BlockedAreas=[new() { X=14,Z=10,Width=2,Height=10 }] }),catalog ?? ContentCatalogTests.Load(),
        inventory:Options.Create(new InventoryOptions { Enabled=true }),npc:Options.Create(new NpcOptions { Enabled=true,X=13,Z=13 }),
        worldStory:Options.Create(new WorldStoryOptions { Enabled=true }),crafting:Options.Create(new CraftingOptions { Enabled=true }));
    internal static CraftOutcome Do(ServerWorld w,int connection,ulong operation,CraftAction action,ushort target)
    { Assert.True(w.TryQueueCraft(connection,new(operation,action,target))); w.Simulate(0.05f); var p=w.Players.Single(p=>p.ConnectionId==connection); return w.CraftResults[p.EntityId].Outcome; }
    internal static CharacterState Materials(ServerWorld w,params SavedMaterial[] amounts)
    { var initial=w.CreateInitialCharacter() with { X=-10,Z=2 }; return initial with { Inventory=initial.Inventory! with { Crafting=new() { LastOperation=0,CooldownSeconds=0,Materials=amounts } } }; }
    private static void Wait(ServerWorld w) => ProfessionTests.Step(w,24);
    [Fact] public void RealGatherRefineAndCraftLoopProducesOneEquipablePersistentInstance()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),w.CreateInitialCharacter() with { X=-11,Z=-7 }); var original=w.CaptureCharacter(42);
        Assert.Equal(CraftOutcome.Accepted,Do(w,42,1,CraftAction.Gather,1)); Wait(w); Assert.Equal(CraftOutcome.Accepted,Do(w,42,2,CraftAction.Gather,1)); Wait(w);
        StarterZoneTests.Walk(w,42,new(-5,-7));
        for(ulong i=3;i<=6;i++) { Assert.Equal(CraftOutcome.Accepted,Do(w,42,i,CraftAction.Gather,2)); Wait(w); }
        Assert.Equal((ushort)14,w.ResourceState(1).Remaining); Assert.Equal((ushort)20,w.ResourceState(2).Remaining);
        StarterZoneTests.Walk(w,42,new(-10,2),2); var xp=w.CaptureCharacter(42).Progression!.Experience;
        Assert.Equal(CraftOutcome.Accepted,Do(w,42,7,CraftAction.Make,1)); Wait(w); Assert.Equal(CraftOutcome.Accepted,Do(w,42,8,CraftAction.Make,1)); Wait(w);
        Assert.Equal(CraftOutcome.Accepted,Do(w,42,9,CraftAction.Make,2)); var saved=w.CaptureCharacter(42);
        Assert.Empty(saved.Inventory!.Crafting!.Materials); Assert.Equal(9UL,saved.Inventory.Crafting.LastOperation);
        var crafted=Assert.Single(saved.Inventory.Items,i=>i.DefinitionId=="crafted_crossing_blade"); Assert.NotEqual(Guid.Empty,crafted.InstanceId); Assert.Equal(EquipmentSlot.None,crafted.EquippedSlot);
        Assert.Equal(original.Inventory!.Items.Length+1,saved.Inventory.Items.Length); Assert.Equal(xp+9,saved.Progression!.Experience);
        var entry=Assert.Single(w.Inventory!.State(p.EntityId,w.Tick).Items,i=>i.Name=="Клинок переправы");
        Assert.True(w.TryQueueInventory(42,new(1,InventoryAction.Equip,entry.Handle))); w.Simulate(0.05f); Assert.Equal(EquipmentSlot.Weapon,Assert.Single(w.CaptureCharacter(42).Inventory!.Items,i=>i.InstanceId==crafted.InstanceId).EquippedSlot);
    }
    [Fact] public void CompetingPlayersCannotBothTakeLastSharedUnitAndReplaySurvivesReconnect()
    {
        var w=World(); w.RestoreWorldNode(new() { Resources=[new(1,1),new(2,24)] },1); var initial=w.CreateInitialCharacter() with { X=-11,Z=-7 };
        var a=w.AddPlayer(42,new(1),initial); var b=w.AddPlayer(43,new(2),initial);
        Assert.True(w.TryQueueCraft(42,new(1,CraftAction.Gather,1))); Assert.True(w.TryQueueCraft(43,new(1,CraftAction.Gather,1))); w.Simulate(0.05f);
        Assert.Equal(CraftOutcome.Accepted,w.CraftResults[a.EntityId].Outcome); Assert.Equal(CraftOutcome.Depleted,w.CraftResults[b.EntityId].Outcome); Assert.Equal((ushort)0,w.ResourceState(1).Remaining);
        Assert.Single(w.CraftState(a.EntityId).Materials); Assert.Empty(w.CraftState(b.EntityId).Materials);
        var saved=w.CaptureCharacter(42); w.RemovePlayer(42); a=w.AddPlayer(42,new(1),saved);
        Assert.Equal(CraftOutcome.AlreadyProcessed,Do(w,42,1,CraftAction.Make,2)); Assert.Equal(saved.Inventory!.Items,w.CaptureCharacter(42).Inventory!.Items); Assert.Single(w.CraftState(a.EntityId).Materials);
        var restart=World(); restart.RestoreWorldNode(w.CaptureWorldNode(),3); Assert.Equal((ushort)0,restart.ResourceState(1).Remaining);
    }
    [Fact] public void RejectedActionsLeaveMaterialsItemsXpStockAndCounterUnchanged()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),Materials(w,new SavedMaterial(1,2),new(3,2))); var before=w.CaptureCharacter(42);
        Assert.Equal(CraftOutcome.TooFar,Do(w,42,1,CraftAction.Gather,1)); Assert.Equal(CraftOutcome.Unavailable,Do(w,42,1,CraftAction.Make,99));
        Assert.Equal(CraftOutcome.InvalidOperation,Do(w,42,9,CraftAction.Make,2)); Assert.False(w.TryQueueCraft(99,new(1,CraftAction.Make,2)));
        var full=Enumerable.Range(0,8).Select(_=>new SavedItem(Guid.NewGuid(),"training_sword",EquipmentSlot.None)).ToArray();
        // Use a known shipped item ID without depending on its spelling.
        full=full.Select(i=>i with { DefinitionId=before.Inventory!.Items[0].DefinitionId }).ToArray();
        w.RemovePlayer(42); p=w.AddPlayer(42,new(1),before with { Inventory=before.Inventory! with { Items=full } });
        Assert.Equal(CraftOutcome.InventoryFull,Do(w,42,1,CraftAction.Make,2)); Assert.Equal(before.Inventory!.Crafting!.Materials,w.CaptureCharacter(42).Inventory!.Crafting!.Materials); Assert.Equal(0UL,w.CraftState(p.EntityId).LastOperation);
        w.RemovePlayer(42); p=w.AddPlayer(42,new(1),before with { Health=0 }); Assert.Equal(CraftOutcome.InvalidState,Do(w,42,1,CraftAction.Make,2)); Assert.Equal(before.Progression!.Serialize(),w.CaptureCharacter(42).Progression!.Serialize());
        w.RemovePlayer(42); p=w.AddPlayer(42,new(1),Materials(w)); Assert.Equal(CraftOutcome.MissingMaterials,Do(w,42,1,CraftAction.Make,1)); Assert.Equal(0UL,w.CraftState(p.EntityId).LastOperation); Assert.Equal((ushort)16,w.ResourceState(1).Remaining);
    }
    [Fact] public void CooldownAndFloodCannotSpendAgainAndLargeMaterialStacksAreBounded()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),w.CreateInitialCharacter() with { X=-11,Z=-7 });
        Assert.Equal(CraftOutcome.Accepted,Do(w,42,1,CraftAction.Gather,1)); Assert.Equal(CraftOutcome.Cooldown,Do(w,42,2,CraftAction.Gather,1)); Wait(w);
        Assert.True(w.TryQueueCraft(42,new(2,CraftAction.Gather,1))); Assert.False(w.TryQueueCraft(42,new(2,CraftAction.Gather,1))); w.Simulate(0.05f); Assert.Equal(CraftOutcome.RateLimited,w.CraftResults[p.EntityId].Outcome); Assert.Equal((ushort)15,w.ResourceState(1).Remaining);
        var saved=w.CaptureCharacter(42); w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved with { Inventory=saved.Inventory! with { Crafting=new() { LastOperation=1,CooldownSeconds=0,Materials=[new(1,999)] } } });
        Assert.Equal(CraftOutcome.MaterialFull,Do(w,42,2,CraftAction.Gather,1)); Assert.Equal((ushort)15,w.ResourceState(1).Remaining);
    }
    [Fact] public void LegacyAndCorruptEconomyModelsAndDefinitionReferencesAreValidated()
    {
        var w=World(); var initial=w.CreateInitialCharacter(); Assert.Null(SavedInventory.Deserialize(initial.Inventory!.Serialize()).Crafting);
        var saved=Materials(w,new SavedMaterial(1,2)).Inventory!; var json=JsonNode.Parse(saved.Serialize())!;
        json["Crafting"]!["LastOperation"]=ulong.MaxValue; Assert.Throws<InvalidDataException>(()=>SavedInventory.Deserialize(json.ToJsonString()));
        json["Crafting"]=null; Assert.Throws<InvalidDataException>(()=>SavedInventory.Deserialize(json.ToJsonString()));
        json["Crafting"]=JsonNode.Parse("{\"Version\":1}"); Assert.Throws<JsonException>(()=>SavedInventory.Deserialize(json.ToJsonString()));
        Assert.Throws<JsonException>(()=>SavedWorldNode.Deserialize("{\"Version\":1,\"Repairs\":0,\"Patrols\":0,\"StormRumor\":false,\"Resources\":[{\"Id\":1}]}"));
        Assert.Throws<InvalidDataException>(()=>SavedWorldNode.Deserialize("{\"Version\":1,\"Repairs\":0,\"Patrols\":0,\"StormRumor\":false,\"Resources\":[{\"Id\":1,\"Id\":2,\"Remaining\":0}]}"));
        Assert.Throws<InvalidDataException>(()=>w.AddPlayer(42,new(1),initial with { Inventory=saved with { Crafting=new() { LastOperation=0,CooldownSeconds=0,Materials=[new(99,1)] } } }));
        var content=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!; content["crafting"]!["recipes"]![0]!["costs"]![0]!["id"]=999;
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(content.ToJsonString())));
    }
}
