using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Shared.Navigation;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ProjectileSnapshotTests
{
    private sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly ServerPlayer Source,Target;
        internal readonly ContentCatalog Catalog;
        internal CombatSimulation Combat=>World.Combat!;
        internal Combatant Attacker=>Combat.Get(Source.EntityId);
        internal Combatant Victim=>Combat.Get(Target.EntityId);
        internal readonly List<AbilityHit> Hits=[];
        internal Fight(double preparation=0,double hitRoll=.5,double criticalRoll=.5,bool payload=false,CrowdControlKind control=CrowdControlKind.Root)
        {
            var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            var skill=data["abilities"]![0]!;skill["power"]=0;skill["magicAttackScale"]=1;
            skill["speed"]=2;skill["range"]=6;skill["radius"]=.1;
            skill["action"]!["form"]=preparation==0?"Instant":"Cast";
            skill["action"]!["preparation"]!["seconds"]=preparation;skill["action"]!["preparation"]!["speed"]="None";
            skill["action"]!["active"]=new JsonObject();
            data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"snapshot_shield","durationSeconds":5,"shield":{"power":{"flat":10}}}"""));
            data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"snapshot_dot","durationSeconds":1,"disposition":"Harmful","periodic":{"kind":"Damage","intervalSeconds":1,"power":{"magicScale":1},"damageFamily":"Magical"}}"""));
            if(payload)
            {
                var definition=new JsonObject{["kind"]=control.ToString()};
                if(control is CrowdControlKind.Knockback or CrowdControlKind.Pull)
                {definition["distance"]=1;definition["speed"]=2;definition["origin"]="Source";}
                else definition["durationSeconds"]=.5;
                if(control==CrowdControlKind.Knockup){definition["height"]=1;definition["downSeconds"]=.5;}
                skill["effects"]=new JsonArray(
                    new JsonObject{["operation"]="Timed",["trigger"]="HealthDamage",["target"]="HitTarget",["effectId"]="snapshot_dot"},
                    new JsonObject{["operation"]="Control",["trigger"]="HealthDamage",["target"]="HitTarget",["control"]=definition});
            }
            Catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Catalog,
                hitRoll:()=>hitRoll,criticalRoll:()=>criticalRoll);
            Source=World.AddPlayer(42,new(1),World.CreateInitialCharacter() with {X=-7,Z=0});
            Target=World.AddPlayer(43,new(2),World.CreateInitialCharacter() with {X=-4,Z=0});
            Combat.DamagePermission=(_,_)=>true;
            SourceStats(Attacker.Stats with {MagicAttack=50,Accuracy=1000,CriticalChance=1,CriticalDamage=2});
            TargetStats(Victim.Stats with {MaxHealth=10000,MagicDefense=0,Evasion=0,HealthRecovery=0});Victim.Health=10000;
        }
        internal void SourceStats(DerivedStats stats)=>Combat.ApplyEquipment(Source.EntityId,(stats,Attacker.Weapon,Attacker.AttackInterval));
        internal void TargetStats(DerivedStats stats)=>Combat.ApplyEquipment(Target.EntityId,(stats,Victim.Weapon,Victim.AttackInterval));
        internal void Step(bool block=false)
        {
            if(block)Assert.True(World.TryQueueDefense(Target.ConnectionId,new(World.Tick+1,DefenseAction.Block,-Vector2.UnitX)));
            World.Simulate(.05f);Hits.AddRange(World.Abilities!.Hits);
        }
        internal void Cast()
        {
            Assert.True(World.TryQueueAbility(Source.ConnectionId,new(1,World.Tick,1,Vector2.UnitX),0));Step();
            Assert.Equal(AbilityOutcome.Accepted,World.Abilities!.Results[Source.EntityId].Outcome);
        }
        internal void Until(Func<bool> predicate,bool block=false)
        {for(var i=0;i<100&&!predicate();i++)Step(block);Assert.True(predicate());}
        internal AbilityHit Impact(bool block=false){Until(()=>Hits.Count>0,block);return Assert.Single(Hits);}
    }

    [Fact]public void ReleasedProjectileKeepsSourcePowerAccuracyAndCriticalParameters()
    {
        var f=new Fight();f.Cast();Assert.Equal(AbilityPhase.Flying,Assert.Single(f.World.Abilities!.ActiveStates).Phase);
        f.SourceStats(f.Attacker.Stats with {MagicAttack=1,Accuracy=-10000,CriticalChance=0,CriticalDamage=100});
        Assert.True(f.Combat.ApplyStatusControl(f.Target.EntityId,f.Source.EntityId,2,1,CrowdControlKind.Blind,3,10000));
        var hit=f.Impact();Assert.False(hit.Evaded);Assert.True(hit.Critical);Assert.Equal(100,hit.Damage,8);
    }

    [Fact]public void CastCapturesAtReleaseRatherThanWhenPreparationStarts()
    {
        var f=new Fight(.25);f.SourceStats(f.Attacker.Stats with {MagicAttack=1,CriticalChance=0});f.Cast();
        Assert.Equal(AbilityPhase.Telegraph,Assert.Single(f.World.Abilities!.ActiveStates).Phase);
        f.SourceStats(f.Attacker.Stats with {MagicAttack=40,CriticalChance=1,CriticalDamage=3});
        f.Until(()=>f.World.Abilities!.ActiveStates.Any(s=>s.Phase==AbilityPhase.Flying));
        f.SourceStats(f.Attacker.Stats with {MagicAttack=500,CriticalChance=0});
        var hit=f.Impact();Assert.True(hit.Critical);Assert.Equal(120,hit.Damage,8);
    }

    [Fact]public void BlindAtReleaseRemainsInTheProjectileAfterCleanse()
    {
        var f=new Fight();f.SourceStats(f.Attacker.Stats with {Accuracy=0});
        Assert.True(f.Combat.ApplyStatusControl(f.Target.EntityId,f.Source.EntityId,2,1,CrowdControlKind.Blind,.5,600));
        f.Cast();Assert.True(f.Combat.RemoveControl(f.Source.EntityId,CrowdControlKind.Blind));
        var hit=f.Impact();Assert.True(hit.Evaded);Assert.Equal(0,hit.Damage);
    }

    [Fact]public void TargetsNewEvasionIsUsedAtContact()
    {
        var f=new Fight();f.Cast();f.TargetStats(f.Victim.Stats with {Evasion=10000});
        var hit=f.Impact();Assert.True(hit.Evaded);Assert.False(hit.Critical);Assert.Equal(0,hit.Damage);
    }

    [Fact]public void TargetsNewDefenseIsUsedAtContact()
    {
        var f=new Fight();f.Cast();f.TargetStats(f.Victim.Stats with {MagicDefense=100});
        var hit=f.Impact();Assert.Equal(new StatCalculator(f.Catalog.Balance).ApplyDefense(100,100),hit.Damage,8);
    }

    [Fact]public void LateBlockUsesReleasedPressureAndLateShieldAbsorbsAfterBlock()
    {
        var f=new Fight();f.SourceStats(f.Attacker.Stats with {CriticalChance=0});f.Cast();
        f.SourceStats(f.Attacker.Stats with {MagicAttack=10000});
        f.TargetStats(f.Victim.Stats with {BlockDamage=.5,BlockStrength=100});
        Assert.True(f.Combat.ApplyEffect(f.Target.EntityId,f.Target.EntityId,"snapshot_shield"));
        f.Until(()=>f.Hits.Count>0,block:true);var hit=Assert.Single(f.Hits);
        Assert.Equal(GuardImpact.Blocked,hit.Guard);Assert.Equal(15,hit.Damage,8);
        Assert.Equal(0,Assert.Single(f.Victim.Effects.Values).ShieldRemaining);
        var heldSeconds=(f.World.Tick-2)*.05;
        Assert.Equal(100-heldSeconds*5-5,f.Combat.DefenseState(f.Target.EntityId,f.World.Tick).Stamina,5);
    }

    [Fact]public void GroundAreaStillUsesSourceStatsAtItsOwnContact()
    {
        var f=new Fight();Assert.True(f.World.TryQueueAbility(f.Source.ConnectionId,new(1,0,2,f.Target.Position),0));f.Step();
        f.SourceStats(f.Attacker.Stats with {MagicAttack=200,CriticalChance=0});
        var hit=f.Impact();var definition=f.Catalog.Abilities["test_ground_area"];
        Assert.False(hit.Critical);Assert.Equal(definition.Power+definition.MagicAttackScale*200,hit.Damage,8);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ReleasedProjectileContinuesAfterSourceDeathOrRemoval(bool remove)
    {
        var f=new Fight(payload:true);var identity=f.Attacker.SourceIdentity;f.Cast();
        if(remove)f.World.RemovePlayer(f.Source.ConnectionId);
        else Assert.True(f.Combat.ApplyAbilityDamage(f.Source.EntityId,100000,f.Target.EntityId)>0);
        var hit=f.Impact();Assert.Equal(100,hit.Damage,8);
        var dot=Assert.Single(f.Victim.Effects.Values);Assert.Equal(identity,dot.Key.Source);Assert.Equal(50,dot.Snapshot.Power);
        var root=Assert.Single(f.Victim.Controls.Active);Assert.Equal(identity,root.Key.Source);Assert.Equal(CrowdControlKind.Root,root.Key.Kind);
        var health=f.Victim.Health;f.Until(()=>f.Victim.Health<health);Assert.Equal(50,health-f.Victim.Health,8);
        Assert.Empty(f.World.Abilities!.ActiveStates);
    }

    [Theory][InlineData(CrowdControlKind.Fear)][InlineData(CrowdControlKind.Knockup)]
    [InlineData(CrowdControlKind.Knockback)][InlineData(CrowdControlKind.Pull)]
    public void RemovedSourceStillProvidesIdentityAndOriginForControl(CrowdControlKind kind)
    {
        var f=new Fight(payload:true,control:kind);var identity=f.Attacker.SourceIdentity;f.Cast();f.World.RemovePlayer(f.Source.ConnectionId);
        f.Impact();var applied=Assert.Single(f.Victim.Controls.Active);Assert.Equal(identity,applied.Key.Source);Assert.Equal(kind,applied.Key.Kind);
        if(kind==CrowdControlKind.Fear)Assert.Equal(f.Source.Position,applied.SourcePosition);
        if(kind is CrowdControlKind.Pull or CrowdControlKind.Knockback)
        {var before=f.Target.Position;f.Step();Assert.True(Vector2.DistanceSquared(before,f.Target.Position)>0);}
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ProjectileAreaInterestIsIndependentOfItsSourcesVisibilityOrDespawn(bool remove)
    {
        var f=new Fight();f.Cast();if(remove)f.World.RemovePlayer(f.Source.ConnectionId);f.Step();
        var interest=new AbilityInterestView();f.World.Abilities!.UpdateInterest(interest,[f.Target.EntityId],f.Target.Position,10,12,f.World.Tick);
        var state=Assert.Single(interest.Changes);Assert.Equal(AbilityPhase.Flying,state.Phase);Assert.Equal(f.Source.EntityId,state.ActorId);
        f.Impact();f.World.Abilities.UpdateInterest(interest,[f.Target.EntityId],f.Target.Position,10,12,f.World.Tick);
        Assert.Equal(AbilityPhase.Finished,Assert.Single(interest.Changes).Phase);
    }

    [Fact]
    public void VisibleProjectileDoesNotRevealHiddenCastOriginAndLosLossFinishesItsPresentation()
    {
        var f=new Fight();f.Cast();f.Step();var abilities=f.World.Abilities!;
        var original=Assert.Single(abilities.ActiveStates);
        var interest=new AbilityInterestView();
        abilities.UpdateInterest(interest,[f.Target.EntityId],f.Target.Position,10,12,f.World.Tick,f.World.Navigation);
        var revealed=Assert.Single(interest.Changes);
        Assert.Equal(AbilityPhase.Flying,revealed.Phase);
        Assert.Equal(revealed.Position,revealed.Origin);Assert.Equal(revealed.Height,revealed.OriginHeight);
        Assert.NotEqual(original.Origin,revealed.Origin);
        Assert.NotEmpty(NetworkProtocol.Write(revealed).CopyData());
        var cells=new byte[900];cells[15*30+9]=1;
        var blocked=new NavigationGrid(new(new(-15,-15),1,.45f,30,30,cells));
        abilities.UpdateInterest(interest,[f.Target.EntityId],f.Target.Position,10,12,f.World.Tick,blocked);
        Assert.Equal(AbilityPhase.Finished,Assert.Single(interest.Changes).Phase);
        abilities.UpdateInterest(interest,[f.Target.EntityId],f.Target.Position,10,12,f.World.Tick,blocked);
        Assert.Empty(interest.Changes);
        abilities.UpdateInterest(interest,[f.Target.EntityId],f.Target.Position,10,12,f.World.Tick,f.World.Navigation);
        Assert.Equal(AbilityPhase.Flying,Assert.Single(interest.Changes).Phase);
    }

    [Fact]public void AProjectileIsNotWithdrawnByCancelOfItsAlreadyCompletedStartup()
    {
        var f=new Fight();f.Cast();Assert.True(f.World.Abilities!.QueueCancel(f.Source.EntityId,new(1,1),f.World.Tick));f.Step();
        Assert.Equal(AbilityOutcome.CancelDenied,f.World.Abilities.Results[f.Source.EntityId].Outcome);Assert.Equal(100,f.Impact().Damage,8);
    }

    [Fact]public void SourceMovingBehindTheTargetDoesNotChangeTheProjectilesIncomingSide()
    {
        var f=new Fight();f.SourceStats(f.Attacker.Stats with {CriticalChance=0});f.Cast();
        f.TargetStats(f.Victim.Stats with {BlockDamage=.5,BlockStrength=100});
        f.Combat.Move(f.Source.EntityId,f.Target.Position+Vector2.UnitX*3);
        Assert.Equal(GuardImpact.Blocked,f.Impact(block:true).Guard);
    }

    [Theory][InlineData(false,70)][InlineData(true,100)]
    public void ReleasedPowerKeepsTheTauntPenaltyAndItsExceptionAfterTauntExpires(bool shootTaunter,double expected)
    {
        var f=new Fight();var taunter=shootTaunter?f.Target.EntityId:f.World.TrainingTargetId;
        Assert.True(f.Combat.ApplyStatusControl(taunter,f.Source.EntityId,2,1,CrowdControlKind.Taunt,.5,.3));f.Cast();
        var hit=f.Impact();Assert.False(f.Attacker.Controls.Has(CrowdControlKind.Taunt));Assert.Equal(expected,hit.Damage,8);
    }
}
