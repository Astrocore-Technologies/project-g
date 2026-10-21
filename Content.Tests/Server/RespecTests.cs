using System.Text;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Xunit;
namespace Content.Tests.Server;

public sealed class RespecTests
{
    internal static ServerWorld World()
    {
        var data=CommonResourceTests.Document();data["creatures"]![0]!["baseHealthRecovery"]=0;data["balance"]!["healthRecovery"]!["vitality"]=0;
        data["creatures"]![0]!["baseManaRecovery"]=0;data["balance"]!["manaRecovery"]!["wisdom"]=0;
        data["balance"]!["stamina"]!["vitality"]=2;
        var w=PvpTests.World(ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));w.ConfigureTaverns(TavernRespawnTests.Catalog());
        w.ConfigureRespec(new(1,1,100,15,["start","forest"]));return w;
    }
    internal static CharacterState Initial(ServerWorld w,int coins=250,int bonus=7)
    {
        // Historical V1 fixture only: current gameplay no longer awards stat points for achievements.
        var s=PvpTests.Initial(w,-5,-4);var p=s.Progression!;
        var invested=new StatInvestment(2,0,6,0,0,0,4);
        return s with {Stats=invested.Apply(p.Allocation!.StartingStats),Progression=p with
        {StatPoints=p.StatPoints-12+bonus,Allocation=p.Allocation! with {Invested=invested,AchievementBonusPoints=bonus}},
            Inventory=s.Inventory! with {Economy=SavedEconomy.Empty with {Coins=coins}}};
    }
    internal static NetworkEntityId Inn(ServerWorld w)
    {
        for(ulong i=1;i<32;i++)if(w.TryGetQuestNpc(new(i),out var npc)&&npc.Name=="Лесная таверна")return new(i);
        throw new Exception("Missing service NPC.");
    }
    private static void Step(ServerWorld w,RespecCommand c)
    {w.ClearLifeResults();Assert.True(w.TryQueueRespec(42,c));w.Simulate(.05f);}
    private static RespecQuote Quote(ServerWorld w,uint seq=1)
    {Step(w,new(seq,RespecAction.Quote,Inn(w)));return w.RespecQuotes[w.GetPlayer(42).EntityId];}
    private static RespecResult Confirm(ServerWorld w,RespecQuote q,uint seq=2)
    {Step(w,new(seq,RespecAction.Confirm,q.NpcId,q.QuoteId,q.Operation));return w.RespecResults[w.GetPlayer(42).EntityId];}

    [Fact] public void FirstResetIsFreeReturnsAllInvestmentsAndPreservesResourcesSkillsAndItems()
    {
        var w=World();var initial=Initial(w);initial=initial with {AttackCooldownSeconds=20,Cooldowns=initial.Cooldowns.Select(c=>c with {Seconds=5}).ToArray()};
        var player=w.AddPlayer(42,new(1),initial);var id=player.EntityId;var actor=w.Combat!.Get(id);
        actor.Health=actor.Stats.MaxHealth*.4;actor.Resources.Mana=actor.Stats.MaxMana*.3;
        w.Combat.RestoreDefense(id,new(1,actor.Stats.MaxStamina*.5,0,1),0);
        var before=w.CaptureCharacter(42);var q=Quote(w);Assert.True(q.FirstFree);Assert.Equal(0,q.Price);Assert.Equal(12,q.ReturnedPoints);Assert.Equal(27,q.AvailableAfter);
        Assert.Null(w.CaptureCharacter(42).Progression!.Respec);Assert.Equal(before.Stats,player.BaseStats);
        var result=Confirm(w,q);Assert.Equal(RespecOutcome.Accepted,result.Outcome);
        var after=w.CaptureCharacter(42);Assert.Equal(w.CreateInitialCharacter().Stats,after.Stats);
        Assert.Equal(27,after.Progression!.StatPoints);Assert.Equal(7,after.Progression.Allocation!.AchievementBonusPoints);
        Assert.Equal(0,after.Progression.Allocation.Invested.Total);Assert.Equal(1,after.Progression.Respec!.Completed);
        Assert.Equal(before.Progression!.Level,after.Progression.Level);Assert.Equal(before.Progression.Experience,after.Progression.Experience);
        Assert.Equal(before.Progression.Skills,after.Progression.Skills);Assert.Equal(before.Inventory!.Serialize(),after.Inventory!.Serialize());
        Assert.Equal(.4,after.Health/after.ResourceMaxima!.Health,9);Assert.Equal(.3,after.Mana/after.ResourceMaxima.Mana,9);
        Assert.Equal(.5,after.Defense!.Stamina/after.ResourceMaxima.Stamina,9);
        Assert.True(after.AttackCooldownSeconds>19);Assert.All(after.Cooldowns,c=>Assert.True(c.Seconds>4));
        var same=Confirm(w,q,3);Assert.Equal(RespecOutcome.AlreadyProcessed,same.Outcome);Assert.Equal(250,w.CaptureCharacter(42).Inventory!.Economy!.Coins);
        var reloaded=SavedProgression.Deserialize(after.Progression.Serialize());Assert.Equal(after.Progression.Respec,reloaded.Respec);
    }
    [Fact] public void ReconnectKeepsFreeUseAndSecondResetDebitsExactlyOnce()
    {
        var w=World();w.AddPlayer(42,new(1),Initial(w));var first=Quote(w);Assert.Equal(RespecOutcome.Accepted,Confirm(w,first).Outcome);
        var saved=w.CaptureCharacter(42);w.RemovePlayer(42);var id=w.AddPlayer(42,new(2),saved).EntityId;
        Assert.True(w.TryQueueProgression(42,new(1,ProgressionAction.AllocateStats,0,0,0,[0,1,0,0,0,0,0])));w.Simulate(.05f);
        var q=Quote(w,1);Assert.False(q.FirstFree);Assert.Equal(100,q.Price);Assert.Equal(2UL,q.Operation);
        Assert.Equal(RespecOutcome.Accepted,Confirm(w,q).Outcome);Assert.Equal(150,w.CaptureCharacter(42).Inventory!.Economy!.Coins);
        Assert.Equal(RespecOutcome.AlreadyProcessed,Confirm(w,q,3).Outcome);Assert.Equal(150,w.CaptureCharacter(42).Inventory!.Economy!.Coins);
        Assert.Equal(2,w.CaptureCharacter(42).Progression!.Respec!.Completed);
        Assert.Equal(RespecOutcome.StaleQuote,Confirm(w,first,4).Outcome);Assert.Equal(150,w.CaptureCharacter(42).Inventory!.Economy!.Coins);
    }
    [Fact] public void StaleBuildAndExpiredOfferCannotConsumeTheFreeReset()
    {
        var w=World();w.AddPlayer(42,new(1),Initial(w));var q=Quote(w);
        Assert.True(w.TryQueueProgression(42,new(1,ProgressionAction.AllocateStats,0,0,0,[1,0,0,0,0,0,0])));w.Simulate(.05f);
        Assert.Equal(RespecOutcome.StaleQuote,Confirm(w,q).Outcome);Assert.Null(w.CaptureCharacter(42).Progression!.Respec);
        q=Quote(w,3);for(var i=0;i<301;i++)w.Simulate(.05f);
        Assert.Equal(RespecOutcome.StaleQuote,Confirm(w,q,4).Outcome);Assert.Null(w.CaptureCharacter(42).Progression!.Respec);
    }
    [Fact] public void InCombatFarDeadLegacyAndEmptyInvestmentNeverMutate()
    {
        var w=World();var id=w.AddPlayer(42,new(1),Initial(w)).EntityId;w.Combat!.RestoreCombatPresence(id,3,0);
        Step(w,new(1,RespecAction.Quote,Inn(w)));Assert.Equal(RespecOutcome.InCombat,w.RespecResults[id].Outcome);w.Combat.RestoreCombatPresence(id,0,0);
        w.Combat.Move(id,new(-10,0));Step(w,new(2,RespecAction.Quote,Inn(w)));Assert.Equal(RespecOutcome.TooFar,w.RespecResults[id].Outcome);
        Assert.Null(w.CaptureCharacter(42).Progression!.Respec);w.Combat.Move(id,new(-5,-4));w.Combat.PayHealth(id,w.Combat.Get(id).Health,true);
        Step(w,new(3,RespecAction.Quote,Inn(w)));Assert.Equal(RespecOutcome.InvalidState,w.RespecResults[id].Outcome);
        w.RemovePlayer(42);var state=Initial(w);id=w.AddPlayer(42,new(2),state with {Progression=state.Progression! with {Allocation=null}}).EntityId;
        Step(w,new(1,RespecAction.Quote,Inn(w)));Assert.Equal(RespecOutcome.MigrationRequired,w.RespecResults[id].Outcome);
        w.RemovePlayer(42);state=w.CreateInitialCharacter() with {X=-5,Z=-4};id=w.AddPlayer(42,new(3),state).EntityId;
        Step(w,new(1,RespecAction.Quote,Inn(w)));Assert.Equal(RespecOutcome.NothingToReset,w.RespecResults[id].Outcome);Assert.Null(w.CaptureCharacter(42).Progression!.Respec);
    }
    [Fact] public void InsufficientMoneyPreservesThePaidBuildAndReceipt()
    {
        var w=World();var state=Initial(w,50);state=state with {Progression=state.Progression! with {Respec=new(1,1,7,0,12)}};
        w.AddPlayer(42,new(1),state);var q=Quote(w);Assert.Equal(100,q.Price);Assert.Equal(RespecOutcome.InsufficientCoins,Confirm(w,q).Outcome);
        var after=w.CaptureCharacter(42);Assert.Equal(state.Stats,after.Stats);Assert.Equal(50,after.Inventory!.Economy!.Coins);Assert.Equal(state.Progression.Respec,after.Progression!.Respec);
    }

    [Fact] public void SameTickFloodAndForgedConfirmNeverSpendOrReset()
    {
        var w=World();var initial=Initial(w);var id=w.AddPlayer(42,new(1),initial).EntityId;
        Assert.False(w.TryQueueRespec(99,new(1,RespecAction.Quote,Inn(w))));
        Assert.True(w.TryQueueRespec(42,new(1,RespecAction.Quote,Inn(w))));
        Assert.False(w.TryQueueRespec(42,new(2,RespecAction.Quote,Inn(w))));w.Simulate(.05f);
        Assert.Equal(RespecOutcome.RateLimited,w.RespecResults[id].Outcome);Assert.Empty(w.RespecQuotes);
        var q=Quote(w,3);Assert.Equal(RespecOutcome.StaleQuote,Confirm(w,q with {QuoteId=q.QuoteId+1},4).Outcome);
        Assert.Equal(initial.Stats,w.CaptureCharacter(42).Stats);Assert.Null(w.CaptureCharacter(42).Progression!.Respec);
        Assert.Equal(250,w.CaptureCharacter(42).Inventory!.Economy!.Coins);
    }
}
