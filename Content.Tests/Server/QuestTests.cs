using System.Numerics;
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

public sealed class QuestTests
{
    internal static ServerWorld World(NavigationOptions? navigation = null, string region = "prototype") => new(
        Options.Create(new MovementOptions { MinX = -20, MaxX = 20, MinZ = -20, MaxZ = 20, SpawnX = -15, SpawnZ = 18 }),
        Options.Create(new InterestOptions()), navigation is null ? null : Options.Create(navigation), ContentCatalogTests.Load(),
        inventory: Options.Create(new InventoryOptions { Enabled = true }), regionId: region, quests: Options.Create(new QuestOptions { Enabled = true }));
    internal static NetworkEntityId Npc(ServerWorld world, string name)
    {
        // Runtime IDs are intentionally discovered through AOI, not persisted or assumed.
        var view = new InterestView(); world.UpdateInterest(42, view);
        foreach (var id in view.Entered) if (world.TryGetQuestNpc(id, out var npc) && npc.Name == name) return id;
        throw new Exception("NPC outside test observer AOI: " + name);
    }
    internal static QuestOutcome Do(ServerWorld world, uint sequence, QuestAction action, NetworkEntityId npc, int connection = 42)
    {
        Assert.True(world.TryQueueQuest(connection, new(sequence, action, npc))); world.Simulate(.05f);
        return world.QuestReplies[world.GetPlayer(connection).EntityId].Outcome;
    }
    private static CharacterState RoundTrip(CharacterState state) => CharacterState.Deserialize(state.Serialize()) with
    { Inventory = SavedInventory.Deserialize(state.Inventory!.Serialize()), Progression = SavedProgression.Deserialize(state.Progression!.Serialize()) };
    [Fact]
    public void FullDeliveryGrantsAndConsumesOnceAndPreservesReceiptAcrossReconstruction()
    {
        var world = World(); var p = world.AddPlayer(42, new(1), world.CreateInitialCharacter()); var giver = Npc(world, "Лада");
        Assert.Equal(QuestStatus.Unknown, world.QuestJournal(p.EntityId).Status);
        Assert.Equal(QuestOutcome.Accepted, Do(world, 1, QuestAction.Talk, giver)); Assert.Equal(1, world.QuestReplies[p.EntityId].Choices);
        Assert.Null(world.CaptureCharacter(42).Progression!.DeliveryQuest);
        Assert.Equal(QuestOutcome.Accepted, Do(world, 2, QuestAction.Accept, giver));
        Assert.Equal(3, world.QuestJournal(p.EntityId).Carried);
        Assert.Equal(QuestOutcome.AlreadyProcessed, Do(world, 3, QuestAction.Accept, giver));
        Assert.False(world.TryQueueQuest(42, new(3, QuestAction.Accept, giver)));
        var active = RoundTrip(world.CaptureCharacter(42));
        var restored = World(); p = restored.AddPlayer(42, new(1), active with { X = -7, Z = 7 });
        var recipient = Npc(restored, "Орен"); var before = restored.CaptureCharacter(42).Progression!.Experience;
        Assert.Equal(QuestOutcome.Accepted, Do(restored, 1, QuestAction.Deliver, recipient));
        Assert.Equal(before + 20, restored.CaptureCharacter(42).Progression!.Experience);
        Assert.Equal(0, restored.QuestJournal(p.EntityId).Carried); Assert.Equal(QuestStatus.Completed, restored.QuestJournal(p.EntityId).Status);
        var complete = RoundTrip(restored.CaptureCharacter(42));
        restored = World(); restored.AddPlayer(42, new(1), complete); recipient = Npc(restored, "Орен");
        Assert.Equal(QuestOutcome.AlreadyProcessed, Do(restored, 1, QuestAction.Deliver, recipient));
        Assert.Equal(complete.Progression!.Experience, restored.CaptureCharacter(42).Progression!.Experience);
        var otherRegion = World(region: "outskirts"); p = otherRegion.AddPlayer(42, new(1), complete with { RegionId = "outskirts" });
        Assert.Equal(QuestStatus.Completed, otherRegion.QuestJournal(p.EntityId).Status);
        Assert.Equal(QuestOutcome.Accepted, Do(otherRegion, 1, QuestAction.Journal, default));
    }
    [Fact]
    public void AuthorityRangeStateAndMaterialFailuresDoNotCreateAReceipt()
    {
        var world = World(); var initial = world.CreateInitialCharacter(); var p = world.AddPlayer(42, new(1), initial); var giver = Npc(world, "Лада");
        Assert.False(world.TryQueueQuest(99, new(1, QuestAction.Accept, giver)));
        Assert.Equal(QuestOutcome.Unavailable, Do(world, 1, QuestAction.Accept, new(9999)));
        Assert.Equal(QuestOutcome.Unavailable, Do(world, 2, QuestAction.Deliver, giver));
        world.RemovePlayer(42); p = world.AddPlayer(42, new(1), initial with { X = -10 });
        Assert.Equal(QuestOutcome.TooFar, Do(world, 1, QuestAction.Accept, giver));
        world.RemovePlayer(42); p = world.AddPlayer(42, new(1), initial with { Health = 0 });
        Assert.Equal(QuestOutcome.InvalidState, Do(world, 1, QuestAction.Accept, giver));
        Assert.Equal(QuestOutcome.Accepted, Do(world, 2, QuestAction.Journal, default));
        world.RemovePlayer(42); p = world.AddPlayer(42, new(1), initial with { Inventory = initial.Inventory! with { Crafting = new() { LastOperation = 0, CooldownSeconds = 0, Materials = [new(1, 999)] } } });
        Assert.Equal(QuestOutcome.MaterialFull, Do(world, 1, QuestAction.Accept, giver));
        Assert.Null(world.CaptureCharacter(42).Progression!.DeliveryQuest);
        world.RemovePlayer(42); p = world.AddPlayer(42, new(1), initial);
        Assert.True(world.TryApplyMove(42, new(1, 0, new(-10, 18))));
        Assert.Equal(QuestOutcome.Busy, Do(world, 1, QuestAction.Accept, giver));
        Assert.Null(world.CaptureCharacter(42).Progression!.DeliveryQuest);
    }
    [Fact]
    public void MissingWoodAndOtherPlayerCannotFinishPrivateQuestAndFloodIsBounded()
    {
        var world = World(); var initial = world.CreateInitialCharacter(); var p = world.AddPlayer(42, new(1), initial); var giver = Npc(world, "Лада");
        Assert.True(world.TryQueueQuest(42, new(1, QuestAction.Accept, giver)));
        Assert.False(world.TryQueueQuest(42, new(2, QuestAction.Accept, giver))); world.Simulate(.05f);
        var active = world.CaptureCharacter(42);
        world.RemovePlayer(42); p = world.AddPlayer(42, new(1), active with { X = -7, Z = 7, Inventory = active.Inventory! with { Crafting = active.Inventory.Crafting! with { Materials = [] } } });
        var recipient = Npc(world, "Орен");
        world.AddPlayer(43, new(2), initial with { X = -7, Z = 7 });
        Assert.Equal(QuestOutcome.Unavailable, Do(world, 1, QuestAction.Deliver, recipient, 43));
        Assert.Equal(QuestOutcome.MissingMaterials, Do(world, 1, QuestAction.Deliver, recipient));
        Assert.Equal(QuestStatus.Active, world.QuestJournal(p.EntityId).Status);
        Assert.Equal(active.Progression!.Experience, world.CaptureCharacter(42).Progression!.Experience);
        world.RemovePlayer(42); p = world.AddPlayer(42, new(1), active with { X = -7, Z = 7 });
        Assert.Equal(QuestOutcome.Accepted, Do(world, 1, QuestAction.Deliver, recipient));
        Assert.Null(world.CaptureCharacter(43).Progression!.DeliveryQuest);
    }
    [Fact]
    public void WallAndDisconnectCancelInteractionAndAoiDoesNotTreatQuestNpcAsCombatant()
    {
        var world = World(new() { BlockedAreas = [new() { X = 6, Z = 36, Width = 1, Height = 4 }] });
        var p = world.AddPlayer(42, new(1), world.CreateInitialCharacter() with { X = -12.5f }); var giver = Npc(world, "Лада");
        Assert.False(world.Combat!.TryGet(giver, out _));
        Assert.Equal(QuestOutcome.TooFar, Do(world, 1, QuestAction.Accept, giver));
        world.RemovePlayer(42); p = world.AddPlayer(42, new(1), world.CreateInitialCharacter());
        Assert.True(world.TryQueueQuest(42, new(1, QuestAction.Accept, giver))); world.DisconnectPvp(42); world.Simulate(.05f);
        Assert.Null(world.CaptureCharacter(42).Progression!.DeliveryQuest);
    }
    [Fact]
    public void LegacySavesAreSupportedButNullCorruptAndUnknownReceiptsFailClosed()
    {
        var world = World(); var initial = world.CreateInitialCharacter(); var progression = initial.Progression!;
        Assert.Null(SavedProgression.Deserialize(progression.Serialize()).DeliveryQuest);
        foreach (var status in new[] { QuestStatus.Unknown, (QuestStatus)255 })
            Assert.Throws<InvalidDataException>(() => (progression with { DeliveryQuest = new(1, "river-road-supplies", status) }).Serialize());
        var json = JsonNode.Parse(progression.Serialize())!; json["DeliveryQuest"] = null;
        Assert.Throws<InvalidDataException>(() => SavedProgression.Deserialize(json.ToJsonString()));
        json["DeliveryQuest"] = JsonNode.Parse("{\"Version\":1,\"Status\":1}");
        Assert.Throws<JsonException>(() => SavedProgression.Deserialize(json.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => world.AddPlayer(42, new(1), initial with { Progression = progression with { DeliveryQuest = new(1, "missing", QuestStatus.Active) } }));
        var definition = ContentCatalogTests.Load().DeliveryQuest!;
        Assert.Throws<ArgumentException>(() => (definition with { Recipient = definition.Giver }).Validate(ContentCatalogTests.Load()));
        Assert.Throws<ArgumentException>(() => (definition with { MaterialId = 999 }).Validate(ContentCatalogTests.Load()));
    }
}
