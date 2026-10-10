using System.Numerics;
using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Content.Tests.Server;

public sealed class SwordTrainingTests
{
    internal static ServerWorld City() => RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory,"Data/regions.json"),
        new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory,"appsettings.json")).Build(), ContentCatalogTests.Load()).Worlds.Single(w=>w.RegionId=="river_city");
    internal static void Step(ServerWorld w, int count=1) { for(var i=0;i<count;i++) w.Simulate(.05f); }
    private static void Move(ServerWorld w,Vector2 target,uint sequence)
    {
        Assert.True(w.TryApplyMove(42,new(sequence,0,target)));
        for(var i=0;i<1000 && w.GetPlayer(42).Motion.IsMoving;i++) Step(w);
        Assert.InRange(Vector2.Distance(w.GetPlayer(42).Position,target),0,.11f);
    }
    internal static CharacterState Trained(ServerWorld w)
    {
        var state = w.CreateInitialCharacter();
        return state with { X=15,Z=-7,Progression=state.Progression! with { SwordTraining=new(1,100) } };
    }
    private static QuestOutcome Talk(ServerWorld w,uint seq,QuestAction action=QuestAction.Talk) => QuestTests.Do(w,seq,action,w.SwordTrainerId);

    [Fact]
    public void CityContainsThreeReachableDummiesWithTenThousandHealthAndATrainer()
    {
        var w=City(); Assert.Equal(3,w.ArenaDummies.Count); Assert.True(w.SwordTrainerId.IsValid);
        Assert.All(w.ArenaDummies,id=> { Assert.Equal(10000,w.Combat!.Get(id).Health); Assert.False(w.Combat.Get(id).CanBleed); });
        Assert.True(w.TryGetQuestNpc(w.SwordTrainerId,out var npc)); Assert.Equal("Радан",npc.Name);
    }

    [Fact]
    public void OnlyPersonalOrdinaryTrainingSwordDamageCountsAndOfferWaitsForReturn()
    {
        var w=City(); var p=w.AddPlayer(42,new(1),w.CreateInitialCharacter() with {X=15,Z=-7});
        var other=w.AddPlayer(43,new(2),w.CreateInitialCharacter());
        Assert.Equal(QuestOutcome.Accepted,Talk(w,1,QuestAction.TrainingSword));
        Assert.Equal(QuestOutcome.Accepted,Talk(w,2,QuestAction.TrainingSword));
        var inventory=w.CaptureCharacter(42).Inventory!;
        Assert.Single(inventory.Items,i=>i.DefinitionId=="arena_training_sword");
        Assert.True(inventory.Items.Single(i=>i.DefinitionId=="arena_training_sword").Bound);
        Move(w,new(15,-12.5f),1);
        Assert.True(w.TryQueueAttack(42,new(1,0,-Vector2.UnitY,w.TrainingTargetId))); Step(w,22);
        Assert.Equal(0,w.CaptureCharacter(42).Progression!.SwordTraining!.Damage); // fallback sword is not the trainer's item
        Assert.True(w.TryQueueAbility(42,new(1,0,1,-Vector2.UnitY),0)); Step(w,40);
        Assert.Equal(0,w.CaptureCharacter(42).Progression!.SwordTraining!.Damage);
        w.Combat!.ApplyEchoDamage(w.TrainingTargetId,10,p.EntityId); Step(w);
        Assert.Equal(0,w.CaptureCharacter(42).Progression!.SwordTraining!.Damage);
        var handle=w.Inventory!.State(p.EntityId,w.Tick).Items.Single(i=>i.Name=="Тренировочный меч").Handle;
        Assert.True(w.TryQueueInventory(42,new(1,InventoryAction.Equip,handle))); Step(w);
        uint attack=2;
        for(var i=0;i<20 && w.CaptureCharacter(42).Progression!.SwordTraining!.Damage<100;i++)
        { Assert.True(w.TryQueueAttack(42,new(attack++,0,-Vector2.UnitY,w.TrainingTargetId))); Step(w,22); }
        Assert.Equal(100,w.CaptureCharacter(42).Progression!.SwordTraining!.Damage);
        Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).OfferedId);
        Assert.Null(w.CaptureCharacter(43).Progression!.SwordTraining);
        Assert.Equal(QuestOutcome.TooFar,Talk(w,3));
        Move(w,new(15,-7),2);
        Assert.Equal(QuestOutcome.Accepted,Talk(w,4));
        Assert.Equal((ushort)2,w.ProfessionState(p.EntityId,w.Tick).OfferedId);
        Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).ActiveId);
        var prepared=ProfessionTests.Command(w,p,1,ProfessionAction.Prepare,2);
        Assert.Equal(ProfessionOutcome.Prepared,prepared.Outcome);
        Assert.Equal(ProfessionOutcome.Accepted,ProfessionTests.Command(w,p,2,ProfessionAction.Confirm,2,prepared.Confirmation).Outcome);
        var saved=w.CaptureCharacter(42); Assert.Equal((ushort)2,saved.Progression!.Profession.ActiveId);
        Assert.Equal(0,w.ProfessionState(p.EntityId,w.Tick).TrainingRequired);
        Assert.Equal(11,saved.Progression.Skills.Count(s=>s.DefinitionId.StartsWith("sword_")));
        Assert.Equal(8,saved.Progression.Skills.Count(s=>s.Slot!=0));
        Assert.All(saved.Progression.Skills.Where(s=>s.DefinitionId.StartsWith("sword_")),s=>Assert.Equal(1,s.Level));
        Assert.Equal(9,w.Abilities!.Loadout(p.EntityId,w.Tick).Abilities.Count);
        Assert.Equal(20,w.Abilities.Loadout(p.EntityId,w.Tick).Abilities.Single(a=>a.Form==AbilityForm.Dash).Range);
        w.RemovePlayer(42); var restored=City(); restored.AddPlayer(42,new(1),saved);
        Assert.Equal(saved.Progression.Serialize(),restored.CaptureCharacter(42).Progression!.Serialize());
    }

    [Fact]
    public void FullInventoryDistanceForgedActionsAndRemoteConfirmationDoNotGrant()
    {
        var w=City(); var state=Trained(w);
        state=state with { Inventory=state.Inventory! with { Items=Enumerable.Range(0,8).Select(_=>new SavedItem(Guid.NewGuid(),"test_armor_item",EquipmentSlot.None)).ToArray() } };
        var p=w.AddPlayer(42,new(1),state);
        Assert.Equal(QuestOutcome.MaterialFull,Talk(w,1,QuestAction.TrainingSword)); Assert.Equal(8,w.CaptureCharacter(42).Inventory!.Items.Length);
        Assert.Equal(QuestOutcome.Unavailable,Talk(w,2,QuestAction.Deliver));
        Talk(w,3); var prepared=ProfessionTests.Command(w,p,1,ProfessionAction.Prepare,2);
        Move(w,new(15,-12),1);
        Assert.Equal(ProfessionOutcome.Unavailable,ProfessionTests.Command(w,p,2,ProfessionAction.Confirm,2,prepared.Confirmation).Outcome);
        Assert.Equal((ushort)0,w.CaptureCharacter(42).Progression!.Profession.ActiveId);
        Assert.False(w.TryQueueQuest(999,new(1,QuestAction.TrainingSword,w.SwordTrainerId)));
    }

    [Fact]
    public async Task TrainingItemProgressAndProfessionPersistTogetherInSqlite()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(default);
        var w=City(); var initial=w.CreateInitialCharacter() with {X=15,Z=-7};
        var session=await store.OpenAsync("",initial,default); var token=session.IssuedToken;
        w.AddPlayer(42,new(1),session.State); Talk(w,1,QuestAction.TrainingSword);
        var snapshot=w.CaptureCharacter(42);
        snapshot=snapshot with {Progression=snapshot.Progression! with {SwordTraining=new(1,51.25)}};
        await store.SaveAsync([new(session,snapshot)],default); await session.DisposeAsync();
        store=new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(default);
        await using var restored=await store.OpenAsync(token,initial,default);
        w=City(); w.AddPlayer(42,new(1),restored.State); Talk(w,1,QuestAction.TrainingSword);
        Assert.Equal(51.25,w.CaptureCharacter(42).Progression!.SwordTraining!.Damage);
        Assert.Single(w.CaptureCharacter(42).Inventory!.Items,i=>i.DefinitionId=="arena_training_sword");
        Assert.Null(SavedProgression.Deserialize(initial.Progression!.Serialize()).SwordTraining);
        Assert.Throws<InvalidDataException>(()=>new SavedSwordTraining(2,1).Validate());
        Assert.Throws<InvalidDataException>(()=>new SavedSwordTraining(1,double.NaN).Validate());
    }
}
