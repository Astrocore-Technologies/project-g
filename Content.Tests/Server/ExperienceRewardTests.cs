using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Server.World;
using Content.Server.WorldStory;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class ExperienceRewardTests
{
    private static readonly ExperienceRewardsDefinition Rewards=new(1,1,20,40,100,5);
    private static ServerWorld World(bool echoes=false)
    {
        var w=new ServerWorld(Options.Create(new MovementOptions {MinX=-32,MaxX=32,MinZ=-32,MaxZ=32,SpawnX=0}),
            Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),
            npc:Options.Create(new NpcOptions {Enabled=true,X=4,Z=0,AggroRadius=.1f,LeashRadius=60,RespawnSeconds=1}),
            echoes:Options.Create(new EchoOptions {Enabled=echoes}),worldStory:Options.Create(new WorldStoryOptions {Enabled=true}),
            worldNodeDefinition:new WorldNodeDefinition {Key="experience-test",Interactive=false,OpeningCells=[]},hitRoll:()=>0,criticalRoll:()=>1);
        w.ConfigureExperienceRewards(Rewards);w.NpcRespawnClock=()=>100000;return w;
    }
    private static ServerPlayer Player(ServerWorld w,int connection,CharacterState? saved=null)
        =>w.AddPlayer(connection,new((ulong)connection),saved??w.CreateInitialCharacter() with {X=0,Z=0});
    private static int Xp(ServerWorld w,int connection)=>w.CaptureCharacter(connection).Progression!.Experience;
    private static void Hit(ServerWorld w,NetworkEntityId source,double power=1)
        =>Assert.True(w.Combat!.ResolveDirectHit(source,w.Npc!.Id,Vector2.UnitX,power,false).Damage>0);
    private static void Kill(ServerWorld w,NetworkEntityId source)
    {Hit(w,source,100000);w.Simulate(.05f);}
    private static void Step(ServerWorld w,int count=1)
    {for(var i=0;i<count;i++){w.Combat!.ClearResults();w.Abilities!.ClearResults();w.Echoes?.ClearResults();w.Simulate(.05f);}}

    [Fact]public void VictoryAwardsOnceInsteadOfPerHitAndDummyOnlyGrantsPractice()
    {
        var w=World();var p=Player(w,1);Hit(w,p.EntityId);Step(w);Assert.Equal(0,Xp(w,1));
        Assert.True(w.Combat!.ResolveDirectHit(p.EntityId,w.TrainingTargetId,Vector2.UnitY,1,false).Damage>0);
        Step(w);Assert.Equal(0,Xp(w,1));Kill(w,p.EntityId);Assert.Equal(20,Xp(w,1));
        Assert.Equal(0,w.Combat.ApplyAbilityDamage(w.Npc!.Id,1000,p.EntityId));Step(w,20);Assert.Equal(20,Xp(w,1));
        Assert.True(w.Combat.Resurrect(default,w.Npc.Id,1,10));Kill(w,p.EntityId);Assert.Equal(40,Xp(w,1));
        // A genuine spawn replacement is a new life and may reward the same participants again.
        w.NpcRespawnClock=()=>101001;Step(w);Kill(w,p.EntityId);Assert.Equal(60,Xp(w,1));
    }

    [Fact]public void DamageEffectiveHealingAndSuccessfulControlShareEquallyWhileBystandersDoNot()
    {
        var w=World();var a=Player(w,1);var healer=Player(w,2);var controller=Player(w,3);Player(w,4);
        Hit(w,a.EntityId);w.Combat!.Get(a.EntityId).Health-=10;
        Assert.Equal(10,w.Combat.Heal(healer.EntityId,a.EntityId,10,authorizedEffect:true));
        Assert.True(w.Combat.ApplyStatusControl(controller.EntityId,w.Npc!.Id,1,1,CrowdControlKind.Root,1));
        Kill(w,a.EntityId);
        foreach(var connection in new[]{1,2,3}){Assert.Equal(6,Xp(w,connection));Assert.Equal(new(1,"2","3"),w.CaptureCharacter(connection).Progression!.ExperienceFraction);}
        Assert.Equal(0,Xp(w,4));
    }

    [Fact]public void PartyChangeDuringFightCannotRewriteContributionOrAddPassiveShares()
    {
        var w=PvpTests.World();w.ConfigureExperienceRewards(Rewards);w.EnableSocial([]);
        var players=Enumerable.Range(1,3).Select(c=>Player(w,c,w.CreateInitialCharacter() with {X=-5,Z=0})).ToArray();var identities=players.Select(_=>Guid.NewGuid()).ToArray();
        for(var i=0;i<3;i++){w.BindWorldActor(i+1,identities[i]);w.BindSocial(i+1,identities[i]);}
        var social=w.Social!;Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(social,identities[0],SocialAction.Create));
        foreach(var id in identities.Skip(1))
        {
            Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(social,identities[0],SocialAction.Invite,target:id));
            Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(social,id,SocialAction.Accept,invite:Assert.Single(social.Invitations(id))));
        }
        Hit(w,players[1].EntityId);
        Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(social,identities[1],SocialAction.Leave));
        Assert.False(social.SameParty(identities[0],identities[1]));
        Kill(w,players[0].EntityId);Assert.Equal(10,Xp(w,1));Assert.Equal(10,Xp(w,2));Assert.Equal(0,Xp(w,3));
    }

    [Theory][InlineData("dead")][InlineData("offline")][InlineData("distant")][InlineData("unloaded")]
    public void IneligiblePastContributorIsExcludedFromDivisor(string reason)
    {
        var w=World();var a=Player(w,1);var b=Player(w,2);Hit(w,a.EntityId);Hit(w,b.EntityId);
        if(reason=="dead")Assert.True(w.Combat!.PayHealth(b.EntityId,w.Combat.Get(b.EntityId).Health,true));
        if(reason=="offline")w.DisconnectPvp(2);
        if(reason=="unloaded")b.Loaded=false;
        if(reason=="distant"){b.Motion.Reset(new(-30,-30),new(-30,-30));w.Combat!.Move(b.EntityId,b.Position);}
        Kill(w,a.EntityId);Assert.Equal(20,Xp(w,1));Assert.Equal(0,Xp(w,2));
    }

    [Fact]public void OverhealAndReturningHomeDoNotQualifyAndCapDoesNotDiluteOtherRecipients()
    {
        var w=World();var a=Player(w,1);var idle=Player(w,2);Hit(w,idle.EntityId);
        w.Combat!.BeginNpcReturn(w.Npc!.Id);w.Combat.NpcReturnedHome(w.Npc.Id);
        Hit(w,a.EntityId);Assert.Equal(0,w.Combat.Heal(idle.EntityId,a.EntityId,100,authorizedEffect:true));
        var initial=w.CreateInitialCharacter();var capped=Player(w,3,initial with {X=0,Z=0,Progression=initial.Progression! with
            {Level=30,StatPoints=163,Allocation=initial.Progression.Allocation! with {EarnedFromLevels=163}}});
        Hit(w,capped.EntityId);Kill(w,a.EntityId);Assert.Equal(20,Xp(w,1));Assert.Equal(0,Xp(w,2));Assert.Equal(0,Xp(w,3));
    }

    [Fact]public void ExactRemaindersSurviveChangingDivisorsAndNeverLeakAtLevelCap()
    {
        SavedExperienceFraction? remainder=null;long total=0;
        foreach(var divisor in new[]{3,7,6,3,7,6,3,7,6,7,7,7,7})
        {var award=SavedExperienceFraction.Add(remainder,20,divisor);total+=award.Whole;remainder=award.Remainder;}
        Assert.Equal(50,total);Assert.Null(remainder); // 3*(20/3)+3*(20/6)+7*(20/7).
        Assert.InRange(new SavedExperienceFraction(1,new string('9',400),"1"+new string('9',400)).Display(),.499999,.5);
        var w=World();var initial=w.CreateInitialCharacter();var p=Player(w,1,initial with {X=0,Z=0,Progression=initial.Progression! with
            {Level=29,Experience=2899,StatPoints=158,Allocation=initial.Progression.Allocation! with {EarnedFromLevels=158},ExperienceFraction=new(1,"2","3")}});
        var b=Player(w,2);Hit(w,b.EntityId);Kill(w,p.EntityId);var saved=w.CaptureCharacter(1).Progression!;
        Assert.Equal(30,saved.Level);Assert.Equal(0,saved.Experience);Assert.Null(saved.ExperienceFraction);
    }

    [Fact]public void OnlyContributingLiveEchoHasItsOwnShareAndRecallReconnectPreserveIt()
    {
        var w=World(true);var p=Player(w,1,EchoTests.WithEcho(w) with {X=0,Z=0});var other=Player(w,2);
        var body=EchoTests.Summon(w,1);Hit(w,body);Hit(w,other.EntityId);Kill(w,p.EntityId);
        Assert.Equal(6,Xp(w,1));Assert.Equal(6,Xp(w,2));var echo=Assert.Single(w.CaptureCharacter(1).OwnedEchoes!);
        Assert.Equal(6,echo.Experience);Assert.Equal(new(1,"2","3"),echo.ExperienceFraction);
        Assert.Equal(6,Assert.Single(w.Echoes!.Roster(p.EntityId,w.Tick).Entries).Experience);
        // Owner has earned only one share, even though their Echo's contribution credits the owner root.
        var saved=w.CaptureCharacter(1);w.RemovePlayer(1);p=Player(w,1,saved);
        echo=Assert.Single(w.CaptureCharacter(1).OwnedEchoes!);Assert.Equal(6,echo.Experience);Assert.Equal(new(1,"2","3"),echo.ExperienceFraction);
        Assert.Equal(new(1,"2","3"),w.CaptureCharacter(1).Progression!.ExperienceFraction);
        Step(w,201);var row=Assert.Single(w.Echoes.Roster(p.EntityId,w.Tick).Entries);
        Assert.True(w.TryQueueEchoRecall(1,new(1,row.Handle)));Step(w);
        Assert.Equal(6,Assert.Single(w.CaptureCharacter(1).OwnedEchoes!).Experience);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void DeadOrUninvolvedEchoDoesNotTakeShare(bool dead)
    {
        var w=World(true);var p=Player(w,1,EchoTests.WithEcho(w) with {X=0,Z=0});var body=EchoTests.Summon(w,1);
        if(dead){Hit(w,body);Assert.True(w.Combat!.PayHealth(body,w.Combat.Get(body).Health,true));}
        Kill(w,p.EntityId);Assert.Equal(20,Xp(w,1));Assert.Equal(0,Assert.Single(w.CaptureCharacter(1).OwnedEchoes!).Experience);
    }

    [Fact]public void DelayedProjectileKeepsOwnerCreditAfterEchoBodyIsGoneWithoutCreatingPhantomEchoShare()
    {
        var w=World(true);var owner=Player(w,1,EchoTests.WithEcho(w) with {X=0,Z=0});var body=EchoTests.Summon(w,1);
        var released=w.Combat!.CaptureReleasedSource(body,1);var offense=w.Combat.CaptureDirectHit(body,100000);
        Assert.True(w.Combat.PayHealth(body,w.Combat.Get(body).Health,true));Step(w);
        Assert.False(w.Combat.TryGet(body,out _));
        Assert.True(w.Combat.ResolveDirectHit(body,w.Npc!.Id,Vector2.UnitX,100000,false,snapshot:offense,releasedSource:released).Damage>0);
        Step(w);Assert.Equal(20,Xp(w,1));Assert.Equal(0,Assert.Single(w.CaptureCharacter(1).OwnedEchoes!).Experience);
        Assert.Equal(w.Combat.SourceIdentity(owner.EntityId),released.ContributionCredit);
    }

    [Fact]public void ExplorationRewardsOnlyNewWalkableCellsAndNeverReplaysOnIdleOrReconnect()
    {
        var w=StarterZoneTests.World();w.ConfigureExperienceRewards(Rewards);var p=Player(w,1,w.CreateInitialCharacter());Step(w);
        var initial=w.CaptureCharacter(1).Progression!.Exploration!;
        Assert.Equal(0,w.CaptureCharacter(1).Progression!.Exploration!.ExperienceCells);
        StarterZoneTests.Walk(w,1,new(9,0));StarterZoneTests.Walk(w,1,new(9,10),2);
        var saved=w.CaptureCharacter(1);var count=saved.Progression!.Exploration!.ExperienceCells;
        var eligible=Enumerable.Range(0,w.Navigation.CellCount).Count(i=>(initial.Cells[i/64]&(1UL<<(i%64)))==0 &&
            (saved.Progression.Exploration.Cells[i/64]&(1UL<<(i%64)))!=0 && w.Navigation.IsWalkable(w.Navigation.Center(i)));
        Assert.Equal(eligible,count);
        Assert.True(count>=100);Assert.Equal(count/100*5+BitOperations.PopCount(saved.Progression.Discoveries)*60,saved.Progression.Experience);
        Step(w,50);Assert.Equal(saved.Progression.Experience,Xp(w,1));
        var next=StarterZoneTests.World();next.ConfigureExperienceRewards(Rewards);p=Player(next,1,saved);Step(next);
        StarterZoneTests.Walk(next,1,new(9,0));Assert.Equal(saved.Progression.Experience,Xp(next,1));
        Assert.Equal(count,next.CaptureCharacter(1).Progression!.Exploration!.ExperienceCells);
    }

    [Fact]public async Task PveAwardAndNpcDeathReceiptShareSqliteCheckpointAcrossRestart()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var w=World();var initial=w.CreateInitialCharacter() with {X=0,Z=0};
        var worldLease=await store.OpenWorldAsync(w.WorldNodeKey,new(),default);w.RestoreWorldNode(worldLease.State,worldLease.Revision);
        var session=await store.OpenAsync("",initial,default);var token=session.IssuedToken;
        var p=Player(w,1,session.State);w.BindWorldActor(1,session.CharacterId);Kill(w,p.EntityId);
        await store.SaveWithWorldAsync([new(session,w.CaptureCharacter(1))],new(worldLease,w.CaptureWorldNode(),w.WorldNodeAudit),default);
        await session.DisposeAsync();await worldLease.DisposeAsync();
        w=World();await using var restoredWorld=await store.OpenWorldAsync(w.WorldNodeKey,new(),default);
        w.RestoreWorldNode(restoredWorld.State,restoredWorld.Revision);await using var restored=await store.OpenAsync(token,initial,default);
        p=Player(w,1,restored.State);Assert.Equal(20,Xp(w,1));Assert.Equal(0,w.Combat!.Get(w.Npc!.Id).Health);
        Step(w,5);Assert.Equal(20,Xp(w,1));
    }

    [Fact]public async Task OneStaleRecipientRollsBackEveryShareAndNpcReceiptTogether()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var w=World();var initial=w.CreateInitialCharacter() with {X=0,Z=0};
        var lease=await store.OpenWorldAsync(w.WorldNodeKey,new(),default);w.RestoreWorldNode(lease.State,lease.Revision);
        var a=await store.OpenAsync("",initial,default);var b=await store.OpenAsync("",initial,default);var at=a.IssuedToken;var bt=b.IssuedToken;
        var first=Player(w,1,a.State);var second=Player(w,2,b.State);Hit(w,first.EntityId);Kill(w,second.EntityId);
        Assert.Equal(10,Xp(w,1));Assert.Equal(10,Xp(w,2));await store.SaveAsync([new(b,b.State)],default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveWithWorldAsync(
            [new(a,w.CaptureCharacter(1)),new(b,w.CaptureCharacter(2))],new(lease,w.CaptureWorldNode(),w.WorldNodeAudit),default));
        await a.DisposeAsync();await b.DisposeAsync();await lease.DisposeAsync();
        await using var restoredA=await store.OpenAsync(at,initial,default);await using var restoredB=await store.OpenAsync(bt,initial,default);
        await using var restoredWorld=await store.OpenWorldAsync(w.WorldNodeKey,new(),default);
        Assert.Equal(0,restoredA.State.Progression!.Experience);Assert.Equal(0,restoredB.State.Progression!.Experience);
        Assert.Null(restoredWorld.State.NpcRespawns);
    }
}
