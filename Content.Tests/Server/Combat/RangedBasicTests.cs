using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class RangedBasicTests
{
    internal sealed class Fight
    {
        internal readonly ContentCatalog Catalog=ContentCatalogTests.Load();
        internal readonly NetworkEntityId Source=new(1),Target=new(2);
        internal readonly SpatialIndex Spatial=new(8);
        internal readonly CombatSimulation Combat;
        internal readonly List<AttackEvent> Hits=new();
        internal readonly List<AbilityEffectState> Flying=new();
        internal readonly HashSet<ulong> Flights=new();
        internal uint Tick;
        internal Combatant Actor=>Combat.Get(Source);
        internal WeaponDefinition Weapon {get;}
        internal double Preparation=>Weapon.AttackIntervalSeconds*Weapon.BasicAttack!.Preparation.Fraction;
        internal double Active=>Weapon.AttackIntervalSeconds*Weapon.BasicAttack!.Active.Fraction;
        internal Fight(string weapon="training_bow",bool playerTarget=false,NavigationOptions? navigation=null)
        {
            Weapon=Catalog.Weapons[weapon];
            Combat=new(Catalog,Spatial,(navigation??new()).CreateGrid(new MovementOptions().ToSettings()),new CombatOptions(),20,8,()=>.5,()=>0);
            Combat.DamagePermission=(source,target)=>source!=target;
            Combat.Add(Source,Vector2.Zero,CombatEntityKind.Player);Spatial.Add(Source,Vector2.Zero);
            Equip(Source,Weapon);
            AddTarget(Target,new(0,6),playerTarget?CombatEntityKind.Player:CombatEntityKind.Monster);
        }
        internal void Equip(NetworkEntityId id,WeaponDefinition weapon)
        {
            var actor=Combat.Get(id);Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=1,HealthRecovery=0,CriticalChance=0},weapon,weapon.AttackIntervalSeconds));
        }
        internal void AddTarget(NetworkEntityId id,Vector2 at,CombatEntityKind kind=CombatEntityKind.Monster)
        {
            Combat.Add(id,at,kind);Spatial.Add(id,at);var actor=Combat.Get(id);
            Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {PhysicalDefense=0,MagicDefense=0,HealthRecovery=0},actor.Weapon,actor.AttackInterval));
        }
        internal void Move(NetworkEntityId id,Vector2 at,float height=0){Combat.Move(id,at,height);Spatial.Move(id,at);}
        internal void Step(float seconds=.05f)
        {
            Combat.Simulate(seconds,++Tick);Hits.AddRange(Combat.Events);Flying.Clear();Combat.PublishBasicProjectiles(Flying,Tick);
            foreach(var state in Flying)Flights.Add(state.EffectId);
        }
        internal void Begin(BasicAttackMode mode=BasicAttackMode.Manual,Vector2? direction=null,float y=0)
        {Assert.True(Combat.Queue(Source,new(1,0,direction??Vector2.UnitY,mode,y),Tick));Step();}
    }

    [Theory][InlineData("training_bow",.3)][InlineData("training_staff",.4)]
    public void BasicReleasesOnceAtActiveAndHasNoResourcePriceOrAutomaticRepeat(string weapon,double fraction)
    {
        var f=new Fight(weapon);var mana=f.Actor.Resources.Mana;var stamina=f.Actor.Resources.Stamina;
        f.Begin();var action=f.Actor.Action!;Assert.Equal(CombatActionCategory.BasicAttack,action.Definition.Category);
        Assert.Equal(f.Weapon.AttackIntervalSeconds*fraction,action.Durations.Preparation,10);
        f.Step((float)(f.Preparation-.01));Assert.Empty(f.Flying);Assert.Empty(f.Hits);
        f.Step(.02f);var shot=Assert.Single(f.Flying);Assert.Equal(0,shot.AbilityId);Assert.Equal(1,shot.OriginHeight);
        Assert.Equal(f.Weapon.BasicProjectile!.Speed,shot.Speed);Assert.Equal(f.Weapon.BasicProjectile.Radius,shot.Radius);
        Assert.NotEmpty(NetworkProtocol.Write(f.Combat.State(f.Source,f.Tick)).CopyData());
        f.Step(3);Assert.Single(f.Hits);Assert.Equal(BasicAttackDelivery.Projectile,f.Hits[0].Delivery);
        Assert.Equal(f.Target,f.Hits[0].TargetId);Assert.True(f.Hits[0].Damage>0);Assert.Null(f.Actor.Action);
        Assert.Equal(mana,f.Actor.Resources.Mana);Assert.Equal(stamina,f.Actor.Resources.Stamina);
    }

    [Theory][InlineData("training_bow")][InlineData("training_staff")]
    public void CancellationBeforeReleaseCreatesNothingAndAfterReleasePreservesFlightAndFullInterval(string weapon)
    {
        var before=new Fight(weapon);before.Begin();var ready=before.Actor.ReadyAt;
        Assert.True(before.Combat.StopBasic(before.Source,ActionStopReason.Voluntary));before.Step(3);
        Assert.Empty(before.Hits);Assert.Empty(before.Flying);Assert.Equal(ready,before.Actor.ReadyAt);
        var after=new Fight(weapon);after.Begin();after.Step((float)(after.Preparation+after.Active+.01));
        ready=after.Actor.ReadyAt;Assert.Equal(CombatActionPhase.Recovery,after.Actor.Action!.Phase);
        Assert.True(after.Combat.StopBasic(after.Source,ActionStopReason.Voluntary));after.Step(1);
        Assert.Single(after.Hits);Assert.True(after.Hits[0].Damage>0);Assert.Equal(ready,after.Actor.ReadyAt);
    }

    [Fact]public void OnlyTheFirstBodyAlongTheFlightReceivesAContact()
    {
        var f=new Fight();f.AddTarget(new(3),new(0,4));var farHp=f.Combat.Get(f.Target).Health;
        f.Begin();f.Step(2);Assert.Equal(new NetworkEntityId(3),Assert.Single(f.Hits).TargetId);Assert.Equal(farHp,f.Combat.Get(f.Target).Health);
    }

    [Theory][InlineData("training_bow",false)][InlineData("training_staff",true)]
    public void ReleaseSnapshotsOffenseWhileTheTargetsDefenseIsLive(string weapon,bool magic)
    {
        var f=new Fight(weapon);var power=new StatCalculator(f.Catalog.Balance).WeaponPower(f.Weapon,f.Actor.Stats);
        f.Begin();f.Step((float)(f.Preparation+.01));Assert.Single(f.Flying);
        var source=f.Actor;f.Combat.ApplyEquipment(f.Source,(source.BaseDerivedStats with {RangedAttack=0,MagicAttack=0,Accuracy=-100000,CriticalChance=1,CriticalDamage=100},source.Weapon,source.AttackInterval));
        var target=f.Combat.Get(f.Target);f.Combat.ApplyEquipment(f.Target,(target.BaseDerivedStats with {PhysicalDefense=100,MagicDefense=200,HealthRecovery=0},target.Weapon,target.AttackInterval));
        f.Step(1);var hit=Assert.Single(f.Hits);Assert.False(hit.Evaded);Assert.False(hit.Critical);
        Assert.Equal(new StatCalculator(f.Catalog.Balance).ApplyDefense(power,magic?200:100),hit.Damage,8);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void AReleasedBasicSurvivesTheDeathOrRemovalOfItsSource(bool remove)
    {
        var f=new Fight();f.Begin();f.Step((float)(f.Preparation+.01));Assert.Single(f.Flying);
        if(remove){f.Combat.Remove(f.Source);f.Spatial.Remove(f.Source);}
        else f.Combat.ApplyAbilityDamage(f.Source,100000);
        f.Step(1);Assert.True(Assert.Single(f.Hits).Damage>0);Assert.Equal(f.Source,f.Hits[0].AttackerId);
    }

    [Fact]public void TwoFixedRangedBasicsCanKillEachOtherAtTheSameContactTime()
    {
        var f=new Fight(playerTarget:true);f.Equip(f.Target,f.Weapon);f.Actor.Health=f.Combat.Get(f.Target).Health=1;
        Assert.True(f.Combat.Queue(f.Source,new(1,0,Vector2.UnitY),0));Assert.True(f.Combat.Queue(f.Target,new(1,0,-Vector2.UnitY),0));
        f.Step();f.Step(1);Assert.Equal(2,f.Hits.Count);Assert.All(f.Hits,h=>Assert.Equal(1,h.Damage));
        Assert.Equal(0,f.Actor.Health);Assert.Equal(0,f.Combat.Get(f.Target).Health);
    }

    [Fact]public void AttackSpeedAndThePreparationAimFollowTheExistingBasicContract()
    {
        var f=new Fight();f.Begin(direction:Vector2.UnitX);var action=f.Actor.Action!;var ready=f.Actor.ReadyAt;
        f.Combat.ApplyEquipment(f.Source,(f.Actor.BaseDerivedStats with {AttackSpeedMultiplier=10,HealthRecovery=0,CriticalChance=0},f.Weapon,.12));
        Assert.Equal(ready,f.Actor.ReadyAt);Assert.Equal(.36,action.Durations.Preparation,10);
        Assert.True(f.Combat.QueueBasicControl(f.Source,new(1,1,action.ExecutionId,BasicControlKind.Aim,Vector2.UnitY),f.Tick));
        f.Step(.1f);f.Step(1);Assert.Equal(f.Target,Assert.Single(f.Hits).TargetId);Assert.Equal(ready,f.Actor.ReadyAt);
    }

    [Fact]public void AWorldObstacleStopsTheBasicBeforeTheBodyBehindIt()
    {
        var f=new Fight(navigation:new(){BlockedAreas=[new(){X=14,Z=18,Width=2,Height=1}]});var hp=f.Combat.Get(f.Target).Health;
        f.Begin();f.Step(2);Assert.False(Assert.Single(f.Hits).TargetId.IsValid);Assert.Equal(hp,f.Combat.Get(f.Target).Health);Assert.Empty(f.Flying);
    }

    [Fact]public void ManualPitchReachesAnElevatedBodyWhileAHorizontalBasicPassesUnderIt()
    {
        var flat=new Fight();flat.Move(flat.Target,new(0,6),3);flat.Begin();flat.Step(2);
        Assert.False(Assert.Single(flat.Hits).TargetId.IsValid);
        var aimed=new Fight();aimed.Move(aimed.Target,new(0,6),3);aimed.Begin(direction:new(0,.8944272f),y:.4472136f);aimed.Step(2);
        Assert.Equal(aimed.Target,Assert.Single(aimed.Hits).TargetId);Assert.True(aimed.Hits[0].Damage>0);
    }

    [Fact]public void MovingBodyCcdUsesTheSameTraceAsASkillProjectile()
    {
        var f=new Fight();f.Move(f.Target,new(3,6));f.Begin();f.Step((float)(f.Preparation+.01));
        f.Combat.MaximumTargetSpeed=10;f.Combat.MotionFor=id=>id==f.Target?new(new(-10,0,0),Vector3.Zero):default;
        f.Combat.SimulateFrame(.4f,++f.Tick,delta=>f.Move(f.Target,f.Combat.Get(f.Target).Position+new Vector2(-10*(float)delta,0)));
        Assert.Equal(f.Target,Assert.Single(f.Combat.Events).TargetId);Assert.True(f.Combat.Events[0].Damage>0);
    }

    [Fact]public void AStrictlyVerticalProjectileHasNoInventedFrontalParryDirection()
    {
        var f=new Fight(playerTarget:true);f.Move(f.Target,Vector2.Zero,4);f.Begin(direction:Vector2.Zero,y:1);f.Step((float)(f.Preparation+.01));
        Assert.True(f.Combat.QueueDefense(f.Target,new(1,DefenseAction.Parry,Vector2.UnitY),f.Tick));f.Step();f.Step(.2f);
        var hit=Assert.Single(f.Hits);Assert.Equal(f.Target,hit.TargetId);Assert.Equal(GuardImpact.None,hit.Guard);Assert.True(hit.Damage>0);
        Assert.Equal(CombatHitTags.None,hit.Tags);
    }
}
