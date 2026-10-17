using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class EncounterTests
{
    internal static ContentCatalog Catalog()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"encounter_shield","durationSeconds":10,"disposition":"Beneficial","shield":{"power":{"flat":10}}}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"encounter_dot","durationSeconds":1,"disposition":"Harmful","periodic":{"kind":"Damage","intervalSeconds":0.5,"power":{"flat":10},"damageFamily":"Physical"}}"""));
        return ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    }
    internal sealed class Fight
    {
        internal readonly ServerWorld World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Catalog(),
            npc:Options.Create(new NpcOptions{Enabled=true,X=-4,Z=0,AggroRadius=.1f,LeashRadius=10}),hitRoll:()=>0,criticalRoll:()=>1);
        internal CombatSimulation Combat=>World.Combat!;
        internal NetworkEntityId Npc=>World.Npc!.Id;
        internal NpcEncounter Ledger=>Combat.Encounter(Npc);
        internal readonly NetworkEntityId First,Second,Healer;
        internal Fight()
        {
            First=World.AddPlayer(1,new(1),World.CreateInitialCharacter() with {X=-7,Z=0}).EntityId;
            Second=World.AddPlayer(2,new(2),World.CreateInitialCharacter() with {X=-8,Z=0}).EntityId;
            Healer=World.AddPlayer(3,new(3),World.CreateInitialCharacter() with {X=-9,Z=0}).EntityId;
            foreach(var id in new[]{Npc,First,Second,Healer})
            {
                var actor=Combat.Get(id);Combat.ApplyEquipment(id,(actor.Stats with {PhysicalDefense=0,MagicDefense=0,HealthRecovery=0},actor.Weapon,actor.AttackInterval));
            }
        }
        internal CombatSourceId Identity(NetworkEntityId id)=>Combat.SourceIdentity(id);
        internal double Hit(NetworkEntityId source,double power)=>Combat.ResolveDirectHit(source,Npc,Vector2.UnitX,power,false).Damage;
        internal NetworkEntityId AddNpc(ulong id)
        {
            var entity=new NetworkEntityId(id);Combat.Add(entity,new(-3,0),CombatEntityKind.Monster,"test_creature");
            Combat.ConfigureNpcEncounter(entity,new(-3,0),10);return entity;
        }
    }

    [Fact]public void DamageAddsActualHpToSeparateThreatAndContributionAndUsesStableTies()
    {
        var f=new Fight();Assert.Equal(10,f.Hit(f.First,10));Assert.Equal(20,f.Hit(f.Second,20));
        Assert.Equal(10,f.Ledger.Threat[f.First]);Assert.Equal(20,f.Ledger.Threat[f.Second]);
        Assert.Equal(f.Second,f.Combat.SelectNpcTarget(f.Npc,f.First));Assert.Equal(20,f.Ledger.Contribution.Entries[f.Identity(f.Second)].Damage);
        f.Hit(f.First,10);Assert.Equal(f.Second,f.Combat.SelectNpcTarget(f.Npc,f.Second));
        Assert.Equal(f.First,f.Combat.SelectNpcTarget(f.Npc,default));
    }

    [Fact]public void ShieldsAndOverkillCannotCreateExtraDamageCreditOrThreat()
    {
        var f=new Fight();Assert.True(f.Combat.ApplyEffect(f.Npc,f.Npc,"encounter_shield"));
        Assert.Equal(0,f.Hit(f.First,10));Assert.Equal(0,f.Ledger.Threat[f.First]);Assert.Empty(f.Ledger.Contribution.Entries);
        f.Combat.Get(f.Npc).Health=5;Assert.Equal(5,f.Hit(f.First,1000));
        Assert.Equal(5,f.Ledger.Contribution.Entries[f.Identity(f.First)].Damage);Assert.Empty(f.Ledger.Threat);Assert.True(f.Ledger.Closed);
    }

    [Fact]public void EffectiveHealingReachesOnlyNpcsAlreadyEngagedWithTheRecipient()
    {
        var f=new Fight();var other=f.AddNpc(900);var idle=f.AddNpc(901);
        f.Combat.EngageNpc(f.Npc,f.First);f.Combat.EngageNpc(other,f.First);
        f.Combat.Get(f.First).Health-=10;
        Assert.Equal(10,f.Combat.Heal(f.Healer,f.First,100,allowOtherPlayers:true));
        foreach(var npc in new[]{f.Npc,other})
        {
            var ledger=f.Combat.Encounter(npc);Assert.Equal(5,ledger.Threat[f.Healer]);
            Assert.Equal(10,ledger.Contribution.Entries[f.Identity(f.Healer)].Healing);
        }
        Assert.Empty(f.Combat.Encounter(idle).Threat);Assert.Empty(f.Combat.Encounter(idle).Contribution.Entries);
        Assert.Equal(0,f.Combat.Heal(f.Healer,f.First,100,allowOtherPlayers:true));Assert.Equal(5,f.Ledger.Threat[f.Healer]);
        f.Combat.PayHealth(f.First,10);Assert.Equal(10,f.Combat.Heal(f.First,f.First,10)); // Self-healing uses the same reverse index.
        Assert.Equal(5,f.Ledger.Threat[f.First]);
    }

    [Fact]public void ControlCreditsActualAddedDurationWithoutThreatPointsDuplicatesOrRemovalRefunds()
    {
        var f=new Fight();Assert.True(f.Combat.ApplyStatusControl(f.First,f.Npc,1,1,CrowdControlKind.Root,1));
        var entry=f.Ledger.Contribution.Entries[f.Identity(f.First)];Assert.Equal(1,entry.Controls[CrowdControlKind.Root].Duration);
        Assert.Equal(0,f.Ledger.Threat[f.First]);f.Combat.Simulate(.5f,1);
        var before=f.Combat.Get(f.Npc).Controls.LastEnd(CrowdControlKind.Root);
        Assert.True(f.Combat.ApplyStatusControl(f.First,f.Npc,1,2,CrowdControlKind.Root,2));
        var after=f.Combat.Get(f.Npc).Controls.LastEnd(CrowdControlKind.Root);var credited=entry.Controls[CrowdControlKind.Root];
        Assert.Equal(1+after-before,credited.Duration,10);Assert.Equal(2ul,credited.Applications);
        Assert.False(f.Combat.ApplyStatusControl(f.First,f.Npc,1,2,CrowdControlKind.Root,2));
        Assert.Equal(credited,entry.Controls[CrowdControlKind.Root]);Assert.True(f.Combat.RemoveControl(f.Npc,CrowdControlKind.Root));
        Assert.Equal(credited,entry.Controls[CrowdControlKind.Root]);Assert.Equal(0,f.Ledger.Threat[f.First]);
    }

    [Fact]public void DeadRemovedOrOutsideLeashCandidatesDisappearWithoutDeletingTheirPastContribution()
    {
        var f=new Fight();f.Hit(f.First,10);f.Hit(f.Second,20);
        var credit=f.Identity(f.Second);f.Combat.PayHealth(f.Second,f.Combat.Get(f.Second).Health,true);
        Assert.DoesNotContain(f.Second,f.Ledger.Threat.Keys);Assert.Equal(f.First,f.Combat.SelectNpcTarget(f.Npc,f.Second));
        Assert.Equal(20,f.Ledger.Contribution.Entries[credit].Damage);
        f.Combat.Move(f.First,new(14,14));Assert.False(f.Combat.SelectNpcTarget(f.Npc,f.First).IsValid);Assert.Empty(f.Ledger.Threat);
        Assert.Equal(10,f.Ledger.Contribution.Entries[f.Identity(f.First)].Damage);
        f.Combat.Move(f.First,new(-7,0));f.Hit(f.First,1);f.World.RemovePlayer(1);Assert.Empty(f.Ledger.Threat);
    }

    [Fact]public void ContributionSurvivesRemovedProjectileAndPeriodicSourcesWithoutGivingThemAnAiTarget()
    {
        var f=new Fight();var source=f.First;var identity=f.Identity(source);var released=f.Combat.CaptureReleasedSource(source,1);
        var offense=f.Combat.CaptureDirectHit(source,10);Assert.True(f.Combat.ApplyEffect(source,f.Npc,"encounter_dot"));
        f.World.RemovePlayer(1);var hit=f.Combat.ResolveDirectHit(source,f.Npc,Vector2.UnitX,10,false,snapshot:offense,releasedSource:released);
        Assert.Equal(10,hit.Damage);f.Combat.Simulate(.5f,1);Assert.Equal(20,f.Ledger.Contribution.Entries[identity].Damage);
        Assert.Empty(f.Ledger.Threat);
    }

    [Fact]public void CreditOwnerIsSeparateFromThreatActorAndPersistsOnOrphanEffects()
    {
        var f=new Fight();var owner=f.Identity(f.First);f.Combat.BindSource(f.Second,new(CombatSourceKind.Echo,Guid.NewGuid()));
        f.Combat.BindContributionOwner(f.Second,owner);Assert.Equal(10,f.Hit(f.Second,10));
        Assert.Equal(10,f.Ledger.Threat[f.Second]);Assert.DoesNotContain(f.First,f.Ledger.Threat.Keys);
        Assert.Equal(10,f.Ledger.Contribution.Entries[owner].Damage);
        Assert.True(f.Combat.ApplyEffect(f.Second,f.Npc,"encounter_dot"));var saved=f.Combat.CaptureEffects(f.Npc);
        var restored=Content.Server.Persistence.CombatEffectDocuments.Deserialize(Content.Server.Persistence.CombatEffectDocuments.Serialize(saved));
        var next=new Fight();next.Combat.RestoreEffects(next.Npc,restored,0);next.Combat.Simulate(.5f,1);
        Assert.Equal(10,next.Ledger.Contribution.Entries[owner].Damage);Assert.Empty(next.Ledger.Threat);
    }

    [Fact]public void ReturnClearsTheEncounterAndDamageDuringReturnDoesNotOpenAnotherOneOrHealTheNpc()
    {
        var f=new Fight();f.Hit(f.First,10);var hp=f.Combat.Get(f.Npc).Health;f.Combat.BeginNpcReturn(f.Npc);
        Assert.Empty(f.Ledger.Threat);Assert.Empty(f.Ledger.Contribution.Entries);
        Assert.Equal(10,f.Hit(f.Second,10));Assert.Equal(hp-10,f.Combat.Get(f.Npc).Health);
        Assert.Empty(f.Ledger.Threat);Assert.Empty(f.Ledger.Contribution.Entries);
        f.Combat.NpcReturnedHome(f.Npc);f.Hit(f.First,1);Assert.Equal(1,f.Ledger.Threat[f.First]);
    }

    [Fact]public void AuthoredThreatIsIndependentOfDamageContributionAndDoesNotAffectPvpTargets()
    {
        var f=new Fight();Assert.True(f.Combat.AddAuthoredThreat(f.First,f.Npc,50));
        Assert.Equal(50,f.Ledger.Threat[f.First]);Assert.Empty(f.Ledger.Contribution.Entries);
        Assert.False(f.Combat.AddAuthoredThreat(f.First,f.Second,50));Assert.False(f.Combat.AddAuthoredThreat(f.First,f.Npc,0));
        Assert.Throws<ArgumentException>(()=>f.Combat.AddAuthoredThreat(f.First,f.Npc,double.PositiveInfinity));
        Assert.Throws<ArgumentException>(()=>f.Combat.AddAuthoredThreat(f.First,f.Npc,-1));
    }

    [Fact]public void AThreatPayloadOnARealAreaContactChangesAggroWithoutPretendingToHaveDealtHpDamage()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![1]!;
        skill["power"]=0;skill["magicAttackScale"]=0;skill["radius"]=.5;
        skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
        skill["effects"]=JsonNode.Parse("""[{"operation":"Threat","trigger":"Contact","target":"HitTarget","power":{"flat":50}}]""");
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),
            npc:Options.Create(new NpcOptions{Enabled=true,X=-4,Z=0,AggroRadius=.1f,LeashRadius=10}),hitRoll:()=>0,criticalRoll:()=>1);
        var player=world.AddPlayer(1,new(1),world.CreateInitialCharacter() with {X=-7,Z=0});var npc=world.Npc!.Id;
        var hp=world.Combat!.Get(npc).Health;Assert.True(world.TryQueueAbility(1,new(1,0,2,new(-4,0)),0));world.Simulate(.05f);
        Assert.Equal(50,world.Combat.Encounter(npc).Threat[player.EntityId]);Assert.Equal(hp,world.Combat.Get(npc).Health);
        Assert.Empty(world.Combat.Encounter(npc).Contribution.Entries);
        Assert.Equal(player.EntityId,world.Combat.SelectNpcTarget(npc,default));
    }
}
