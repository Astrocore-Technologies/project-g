using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Content.Server.Regions;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class EchoPermissionTests
{
    private sealed class Decisions : IEchoCombatPermissionSource
    {
        internal int Reads;
        internal readonly Dictionary<Guid,EchoCombatPermission> Values=new();
        public EchoCombatPermission Read(Guid instance){Reads++;return Values.GetValueOrDefault(instance,EchoCombatPermission.AllowAll);}
    }
    private static ContentCatalog Catalog(EchoRefusalActionReaction reaction=EchoRefusalActionReaction.FinishCurrent,
        bool five=true,bool cast=false,bool projectile=false)
    {
        var root=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        if(five){root["echoes"]![0]!["stars"]=5;root["echoes"]![0]!["refusalDuringAction"]=reaction.ToString();}
        var signature=root["abilities"]!.AsArray().Single(a=>a!["id"]!.GetValue<string>()=="mira_signature")!;
        if(cast)signature["action"]=JsonNode.Parse("""
            {"form":"Cast","category":"Skill","capabilities":"Magical","interruptible":true,
             "preparation":{"seconds":1,"speed":"None","cancel":"Voluntary"}}
            """);
        if(projectile)
        {
            signature["kind"]="Projectile";signature.AsObject().Remove("groundVolume");
            signature["projectileLaunchHeight"]=1;signature["speed"]=4;signature["range"]=12;signature["radius"]=.2;
        }
        return ContentCatalog.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
    private static ServerWorld Create(ContentCatalog catalog,Decisions decisions)=>new(Options.Create(new MovementOptions()),
        Options.Create(new InterestOptions()),catalog:catalog,echoes:Options.Create(new EchoOptions{Enabled=true}),
        hitRoll:()=>0,criticalRoll:()=>1,echoPermissions:decisions);
    private static void Step(ServerWorld world,int count=1)
    {for(var i=0;i<count;i++){world.Combat!.ClearResults();world.Abilities!.ClearResults();world.Echoes!.ClearResults();world.Simulate(.05f);}}
    private static (ServerPlayer Owner,NetworkEntityId Body,Guid Instance) Summon(ServerWorld w)
    {
        var owner=w.AddPlayer(1,new(1),EchoTests.WithEcho(w) with {X=-7,Z=2});var id=EchoTests.Summon(w,1);
        w.Combat!.SetManaRecoveryConnected(id,false);
        return(owner,id,Assert.Single(w.CaptureCharacter(1).OwnedEchoes!).InstanceId);
    }

    [Theory][InlineData(EchoRefusalActionReaction.FinishCurrent,1,10)][InlineData(EchoRefusalActionReaction.InterruptAsEnemy,0,5)]
    public void ExplicitPerEchoReactionFinishesOrInterruptsExistingCastUsingItsCommonPayment(
        EchoRefusalActionReaction reaction,int expectedHits,double spent)
    {
        var decisions=new Decisions();var w=Create(Catalog(reaction,cast:true),decisions);var f=Summon(w);
        var before=w.Abilities!.Mana(f.Body);Assert.True(global::Content.Tests.Server.EchoTests.Signature(w,1,new(1,w.Tick,1,new(-7,3))));Step(w);
        Assert.NotNull(w.Combat!.Get(f.Body).Action);Assert.Equal(before,w.Abilities.Mana(f.Body));
        decisions.Values[f.Instance]=new(signatureAllowed:false);Assert.True(w.Echoes!.RefreshPermission(f.Owner.EntityId,f.Instance));
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(w,1,new(2,w.Tick,1,new(-7,3))));Step(w);
        Assert.Equal(EchoCommandOutcome.Refused,w.Echoes.Results[f.Owner.EntityId].Outcome);
        Assert.True(Assert.Single(w.Echoes.Loadout(f.Owner.EntityId,w.Tick).Slots).SignatureRefused);
        Assert.Equal(EchoRefusal.Signature,Assert.Single(w.Echoes.Roster(f.Owner.EntityId,w.Tick).Entries).Refusal);
        var hits=0;
        for(var i=0;i<30;i++){Step(w);hits+=w.Abilities.Hits.Count(h=>h.ActorId==f.Body);}
        Assert.Equal(expectedHits,hits);Assert.Equal(before-spent,w.Abilities.Mana(f.Body));
        Assert.True(Assert.Single(w.Abilities.CaptureCooldowns(f.Body)).Seconds>0);
        Assert.Equal(SavedEchoAvailability.Active,Assert.Single(w.CaptureCharacter(1).OwnedEchoes!).Availability);
    }

    [Theory][InlineData(EchoRefusalActionReaction.FinishCurrent,1)][InlineData(EchoRefusalActionReaction.InterruptAsEnemy,0)]
    public void RefusingBasicStopsFutureIntentAndUsesOnlyThisEchoReactionForCurrentPreparation(
        EchoRefusalActionReaction reaction,int expectedHits)
    {
        var decisions=new Decisions();var w=Create(Catalog(reaction),decisions);var f=Summon(w);
        w.Echoes!.Alert(f.Owner.EntityId,w.TrainingTargetId);Step(w);
        Assert.NotNull(w.Combat!.Get(f.Body).Action);
        decisions.Values[f.Instance]=new(basicAllowed:false);Assert.True(w.Echoes.RefreshPermission(f.Owner.EntityId,f.Instance));
        var hits=0;
        for(var i=0;i<30;i++){Step(w);hits+=w.Combat.Events.Count(h=>h.AttackerId==f.Body&&h.Damage>0);}
        Assert.Equal(expectedHits,hits);Assert.False(w.Combat.HasContinuousBasic(f.Body));
        Assert.Null(w.Combat.Get(f.Body).Action);
        Assert.True(w.Combat.Get(f.Body).Health>0);Assert.Single(w.Echoes.Loadout(f.Owner.EntityId,w.Tick).Slots);
    }

    [Fact]public void CommonSkillRunnerRechecksPermissionAndAllowsResumedHelp()
    {
        var decisions=new Decisions();var w=Create(Catalog(),decisions);var f=Summon(w);var mana=w.Abilities!.Mana(f.Body);
        decisions.Values[f.Instance]=new(refusedSkills:[40]);Assert.True(w.Echoes!.RefreshPermission(f.Owner.EntityId,f.Instance));Step(w);
        Assert.True(w.Abilities.QueueCompanionSignature(f.Owner.EntityId,f.Body,new(1,w.Tick,40,new(-7,3)),w.Tick));Step(w);
        Assert.Equal(AbilityOutcome.InvalidState,w.Abilities.Results[f.Body].Outcome);Assert.Equal(mana,w.Abilities.Mana(f.Body));
        Assert.Empty(w.Abilities.Hits);Assert.Equal(0,Assert.Single(w.Abilities.CaptureCooldowns(f.Body)).Seconds);
        decisions.Values[f.Instance]=EchoCombatPermission.AllowAll;Assert.True(w.Echoes.RefreshPermission(f.Owner.EntityId,f.Instance));
        Assert.True(w.Abilities.QueueCompanionSignature(f.Owner.EntityId,f.Body,new(2,w.Tick,40,new(-7,3)),w.Tick));Step(w);
        Assert.Equal(AbilityOutcome.Accepted,w.Abilities.Results[f.Body].Outcome);Assert.Equal(mana-10,w.Abilities.Mana(f.Body));
    }

    [Fact]public void RefusalAfterReleaseCannotRecallAProjectileOrRepriceItsPaidAction()
    {
        var decisions=new Decisions();var w=Create(Catalog(EchoRefusalActionReaction.InterruptAsEnemy,projectile:true),decisions);var f=Summon(w);
        var actor=w.Combat!.Get(f.Body);var direction=Vector2.Normalize(w.Combat.Get(w.TrainingTargetId).Position-actor.Position);
        var mana=w.Abilities!.Mana(f.Body);Assert.True(global::Content.Tests.Server.EchoTests.Signature(w,1,new(1,w.Tick,1,direction)));Step(w);
        Assert.Contains(w.Abilities.ActiveStates,e=>e.ActorId==f.Body&&e.Phase==AbilityPhase.Flying);
        decisions.Values[f.Instance]=new(skillsAllowed:false);Assert.True(w.Echoes!.RefreshPermission(f.Owner.EntityId,f.Instance));
        var hits=0;for(var i=0;i<25;i++){Step(w);hits+=w.Abilities.Hits.Count(h=>h.ActorId==f.Body&&h.TargetId==w.TrainingTargetId);}
        Assert.Equal(1,hits);Assert.Equal(mana-10,w.Abilities.Mana(f.Body));
    }

    [Fact]public void RelationshipDecisionIsReadFromItsSourceAgainDuringRegionalTransferWithoutLosingOwnedProgress()
    {
        var decisions=new Decisions();var catalog=Catalog();var source=Create(catalog,decisions);source.PvpClock=()=>100000;var f=Summon(source);
        decisions.Values[f.Instance]=new(signatureAllowed:false);Assert.True(source.Echoes!.RefreshPermission(f.Owner.EntityId,f.Instance));Step(source);
        var cargo=source.CaptureCharacter(1);source.RemovePlayer(1);
        var target=Create(catalog,decisions);target.PvpClock=()=>100000;var owner=target.AddPlayer(1,new(2),cargo,restoringFromLogout:false);
        var body=Assert.Single(target.Echoes!.Loadout(owner.EntityId,target.Tick).Slots).EntityId;
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(target,1,new(1,target.Tick,1,new(-7,3))));Step(target);
        Assert.Equal(EchoCommandOutcome.Refused,target.Echoes.Results[owner.EntityId].Outcome);
        Assert.Equal(f.Instance,Assert.Single(target.CaptureCharacter(1).OwnedEchoes!).InstanceId);Assert.True(target.Combat!.Get(body).Health>0);
    }

    [Fact]public void OrdinaryEchoNeverQueriesRelationshipRefusalAndCannotReceiveARefusalNotification()
    {
        var decisions=new Decisions();var w=Create(Catalog(five:false),decisions);var f=Summon(w);
        decisions.Values[f.Instance]=new(false,false,false);Assert.False(w.Echoes!.RefreshPermission(f.Owner.EntityId,f.Instance));
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(w,1,new(1,w.Tick,1,new(-7,3))));Step(w);
        Assert.Equal(EchoCommandOutcome.Accepted,w.Echoes.Results[f.Owner.EntityId].Outcome);Assert.Equal(0,decisions.Reads);
    }
    [Fact]public void OneEchoRefusalCannotAffectAnotherOwnedEchoOrItsResources()
    {
        var decisions=new Decisions();var w=Create(Catalog(),decisions);var first=Guid.NewGuid();var second=Guid.NewGuid();
        var owner=w.AddPlayer(1,new(1),w.CreateInitialCharacter() with {X=-7,Z=2,Echoes=new(){Active=[
            new(first,"test_guardian_echo",1,null,null),new(second,"test_guardian_echo",2,null,null)]}});
        var a=EchoTests.Summon(w,1,first,1);var b=EchoTests.Summon(w,1,second,2);
        decisions.Values[first]=new(false,false,false);Assert.True(w.Echoes!.RefreshPermission(owner.EntityId,first));Step(w);
        var manaA=w.Abilities!.Mana(a);var manaB=w.Abilities.Mana(b);w.Combat!.SetManaRecoveryConnected(a,false);w.Combat.SetManaRecoveryConnected(b,false);
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(w,1,new(1,w.Tick,1,new(-7,3))));Step(w);
        Assert.Equal(EchoCommandOutcome.Refused,w.Echoes.Results[owner.EntityId].Outcome);
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(w,1,new(2,w.Tick,2,new(-7,3))));Step(w);
        Assert.Equal(EchoCommandOutcome.Accepted,w.Echoes.Results[owner.EntityId].Outcome);
        Assert.Equal(manaA,w.Abilities.Mana(a));Assert.Equal(manaB-10,w.Abilities.Mana(b));
        var slots=w.Echoes.Loadout(owner.EntityId,w.Tick).Slots;
        Assert.True(slots.Single(s=>s.EntityId==a).SignatureRefused);Assert.False(slots.Single(s=>s.EntityId==b).SignatureRefused);
    }
    [Fact]public void RegionalFactoryUsesTheSameExternalDecisionSourceInEveryWorld()
    {
        var decisions=new Decisions();var config=new ConfigurationBuilder().AddConfiguration(LegacyRegionFixtures.Configuration())
            .AddInMemoryCollection(new Dictionary<string,string?>{["Echoes:Enabled"]="true"}).Build();
        var regions=RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory,"Fixtures","retired-authored-regions.json"),config,Catalog(),
            hitRoll:()=>0,criticalRoll:()=>1,echoPermissions:decisions);
        foreach(var world in regions.Worlds)
        {
            var state=EchoTests.WithEcho(world);var instance=Assert.Single(state.Echoes!.Active).InstanceId;
            decisions.Values[instance]=new(false,false,false);var owner=world.AddPlayer(1,new(1),state);
            Assert.Equal(EchoRefusal.Basic|EchoRefusal.Signature,Assert.Single(world.Echoes!.Roster(owner.EntityId,0).Entries).Refusal);
            world.RemovePlayer(1);
        }
        Assert.Equal(regions.Worlds.Count,decisions.Reads);
    }
}
