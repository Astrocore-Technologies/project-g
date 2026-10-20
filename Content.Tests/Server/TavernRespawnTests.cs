using System.Numerics;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Server.Respawn;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Xunit;

namespace Content.Tests.Server;

public sealed class TavernRespawnTests
{
    internal static TavernDefinition Inn(string id,ushort network,string region,float x,float z)=>new()
    {Id=id,NetworkId=network,Revision=1,RegionId=region,Name=id=="start"?"Стартовая таверна":"Лесная таверна",Npc=new(x,0,z),Spawn=new(x-1,0,z)};
    internal static TavernCatalog Catalog()=>new("start",[Inn("start",1,"prototype",-8,0),Inn("forest",2,"prototype",-5,-4)]);

    [Theory][InlineData(1000,200,50)][InlineData(1000,20,20)][InlineData(1677,200,83)][InlineData(19,18,0)][InlineData(1000,0,0)]
    public void PvePenaltyUsesWholeThresholdRoundsDownAndNeverCreatesDebt(int threshold,int experience,int expected)
    {
        var curve=new ExperienceCurve(ContentCatalogTests.Load().Progression with {ExperiencePerLevel=threshold});
        Assert.Equal(expected,DeathPenaltyPolicy.PveLoss(curve,1,experience));
        Assert.Equal(0,DeathPenaltyPolicy.PveLoss(curve,curve.LevelCap,0));
    }

    [Fact] public void PveDeathWithoutPvpModuleDebitsOnceAndRetainsReceiptAcrossReconnect()
    {
        var w=CharacterPersistenceTests.World();Assert.False(w.HasPvp);
        var initial=w.CreateInitialCharacter();var p=w.AddPlayer(42,new(1),initial with {Progression=initial.Progression! with {Experience=20}});
        w.Combat!.PayHealth(p.EntityId,w.Combat.Get(p.EntityId).Health,true);
        var saved=w.CaptureCharacter(42);Assert.Equal(15,saved.Progression!.Experience);
        Assert.Equal(new SavedDeathPenalty(1,1,false,5),saved.DeathPenalty);
        Assert.False(w.Combat.PayHealth(p.EntityId,1,true));Assert.Equal(15,w.CaptureCharacter(42).Progression!.Experience);
        w.RemovePlayer(42);var next=w.AddPlayer(42,new(2),saved);
        Assert.Equal(saved.DeathPenalty,w.CaptureCharacter(42).DeathPenalty);Assert.Equal(0,w.Combat.Get(next.EntityId).Health);
        Assert.Equal(15,w.CaptureCharacter(42).Progression!.Experience);
    }

    [Fact] public void DefaultBindingAndFreeNpcServiceValidateStateDistanceCombatAndReplay()
    {
        var w=PvpTests.World();w.ConfigureTaverns(Catalog());var p=w.AddPlayer(42,new(1),PvpTests.Initial(w,-9,0));var id=p.EntityId;
        Assert.Equal("start",w.CaptureCharacter(42).RespawnBinding!.TavernId);
        var candidates=new List<QuestNpcSpawn>();
        for(ulong value=1;value<30;value++)if(w.TryGetQuestNpc(new(value),out var npc)&&npc.Role=="innkeeper")candidates.Add(npc);
        var inn=candidates.Single(n=>n.Name=="Лесная таверна");
        TavernOutcome Bind(uint seq)
        {w.ClearLifeResults();Assert.True(w.TryQueueTavern(42,new(seq,inn.EntityId)));w.Simulate(.05f);return w.TavernResults[id].Outcome;}
        Assert.Equal(TavernOutcome.TooFar,Bind(1));
        w.Combat!.Move(id,new(-5,-4));w.Combat.RestoreCombatPresence(id,2,0);
        Assert.Equal(TavernOutcome.InCombat,Bind(2));w.Combat.RestoreCombatPresence(id,0,0);
        var before=w.CaptureCharacter(42);Assert.Equal(TavernOutcome.Accepted,Bind(3));
        var saved=w.CaptureCharacter(42);Assert.Equal("forest",saved.RespawnBinding!.TavernId);
        Assert.Equal(before.Inventory!.Serialize(),saved.Inventory!.Serialize());Assert.Equal(before.Progression!.Experience,saved.Progression!.Experience);
        Assert.False(w.TryQueueTavern(42,new(3,inn.EntityId)));Assert.Equal(TavernOutcome.AlreadyBound,Bind(4));
        w.RebindConnection(42,43,new(2));w.RebindConnection(43,42,new(3));
        Assert.Equal(TavernOutcome.AlreadyBound,Bind(1));
        var json=CharacterState.Deserialize(saved.Serialize());Assert.Equal(saved.RespawnBinding,json.RespawnBinding);
        w.Combat.PayHealth(id,w.Combat.Get(id).Health,true);Assert.Equal(TavernOutcome.InvalidState,Bind(5));
    }

    [Fact] public void MissingOrMovedTavernFailsClosedInsteadOfChoosingAnotherSpawn()
    {
        var catalog=Catalog();var saved=catalog.Starting.Binding();
        Assert.Throws<InvalidDataException>(()=>catalog.Resolve(saved with {TavernId="removed"}));
        Assert.Throws<InvalidDataException>(()=>catalog.Resolve(saved with {X=saved.X+1}));
        Assert.Throws<InvalidDataException>(()=>catalog.Resolve(saved with {TavernRevision=2}));
        Assert.Throws<InvalidDataException>(()=>new SavedDeathPenalty(1,0,false,1).Validate());
    }

    [Fact] public void SameTickFloodCannotApplyThePendingBindingAfterRateLimit()
    {
        var w=PvpTests.World();w.ConfigureTaverns(Catalog());var p=w.AddPlayer(42,new(1),PvpTests.Initial(w,-5,-4));
        var inn=Enumerable.Range(1,30).Select(value=>new NetworkEntityId((ulong)value))
            .Single(id=>w.TryGetQuestNpc(id,out var npc)&&npc.Name=="Лесная таверна");
        Assert.True(w.TryQueueTavern(42,new(1,inn)));Assert.False(w.TryQueueTavern(42,new(2,inn)));
        w.Simulate(.05f);Assert.Equal(TavernOutcome.RateLimited,w.TavernResults[p.EntityId].Outcome);
        Assert.Equal("start",w.CaptureCharacter(42).RespawnBinding!.TavernId);
        Assert.False(w.TryQueueTavern(42,new(2,inn)));
    }

    [Fact] public async Task FailedRegionalRespawnKeepsTheDurableCorpseAndAlreadyPaidPenalty()
    {
        await using var f=await RegionalTravelTests.Fixture.Create();
        var catalog=new TavernCatalog("start",[Inn("start",1,"outskirts",-8,0)]);
        f.Source.ConfigureTaverns(catalog);f.Target.ConfigureTaverns(catalog);
        var session=await f.Add(x:-7,z:0);var id=f.Source.GetPlayer(42).EntityId;
        f.Source.Combat!.PayHealth(id,f.Source.Combat.Get(id).Health,true);
        var dead=f.Source.CaptureCharacter(42);await f.Store.SaveAsync([new(session,dead)],default);session.Revision++;
        var targetLease=f.Leases[f.Target.WorldNodeKey];
        await f.Store.SaveWithWorldAsync([],new(targetLease,new(){StormRumor=true},
            [new("test","Fence","external revision",f.Now)]),default);
        f.Now+=60001;Assert.True(f.Source.TryQueueRespawn(42,new(1,dead.Life!.DeathId)));f.Realm.Simulate(.05f);
        Assert.True(f.Begin());await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Complete());
        Assert.True(f.Realm.Faulted);Assert.Empty(f.Target.Players);
        var stored=f.Store.SingleState;Assert.True(stored.Life!.Dead);Assert.Equal("prototype",stored.RegionId);
        Assert.Equal(dead.DeathPenalty,stored.DeathPenalty);Assert.Equal(0,stored.Health);
    }

    [Fact] public async Task ForeignTavernRespawnCommitsLifeResourcesReceiptAndRegionTogether()
    {
        await using var f=await RegionalTravelTests.Fixture.Create();
        var catalog=new TavernCatalog("start",[Inn("start",1,"outskirts",-8,0),Inn("forest",2,"prototype",-5,-4)]);
        catalog.ValidateWorlds([f.Source,f.Target]);f.Source.ConfigureTaverns(catalog);f.Target.ConfigureTaverns(catalog);
        var session=await f.Add(x:-7,z:0);var p=f.Source.GetPlayer(42);var id=p.EntityId;
        f.Source.Combat!.PayHealth(id,f.Source.Combat.Get(id).Health,true);
        var dead=f.Source.CaptureCharacter(42);await f.Store.SaveAsync([new(session,dead)],default);session.Revision++;
        f.Now+=60001;Assert.True(f.Source.TryQueueRespawn(42,new(1,dead.Life!.DeathId)));f.Realm.Simulate(.05f);
        var gate=f.Store.PauseSaves();Assert.True(f.Begin());
        try
        {
            Assert.False(f.Realm.TryCompleteTravel(out _));Assert.Empty(f.Target.Players);
            Assert.True(f.Store.SingleState.Life!.Dead);Assert.Equal("prototype",f.Store.SingleState.RegionId);
            gate.TrySetResult();await f.Complete();
            var restored=f.Target.CaptureCharacter(42);Assert.Equal("outskirts",restored.RegionId);Assert.False(restored.Life!.Dead);
            Assert.Equal(dead.Life.DeathId,restored.Life.DeathId);Assert.Equal(dead.DeathPenalty,restored.DeathPenalty);
            Assert.Equal(dead.Progression!.Experience,restored.Progression!.Experience);
            Assert.Equal(new Vector2(-9,0),new(restored.X,restored.Z));
            Assert.Equal(restored.ResourceMaxima!.Health*.5,restored.Health,8);Assert.Equal(restored.ResourceMaxima.Mana*.5,restored.Mana,8);
            Assert.Equal(restored.ResourceMaxima.Stamina,restored.Defense!.Stamina);
            Assert.Equal(RespawnOutcome.Accepted,f.Target.RespawnResults[f.Target.GetPlayer(42).EntityId].Outcome);
            Assert.False(f.Target.TryQueueRespawn(42,new(1,dead.Life.DeathId)));
            Assert.Equal(RespawnOutcome.AlreadyProcessed,f.Target.RespawnResults[f.Target.GetPlayer(42).EntityId].Outcome);
            Assert.Equal("outskirts",f.Store.SingleState.RegionId);Assert.False(f.Store.SingleState.Life!.Dead);
        }
        finally{gate.TrySetResult();}
    }
}
