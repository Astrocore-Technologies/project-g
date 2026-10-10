using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Items;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class SwordsmanSimulationTests
{
    private sealed class Fight
    {
        public readonly NetworkEntityId Player=new(1), Target=new(2);
        public readonly CombatSimulation Combat;
        public readonly AbilitySimulation Abilities;
        public readonly InventorySimulation Inventory;
        public readonly SpatialIndex Spatial=new(8);
        public readonly List<AbilityHit> Hits=[];
        public uint Tick, AbilitySequence, AttackSequence, DefenseSequence;
        public double Damage => 10000-Combat.Get(Target).Health;
        public double Stamina => Combat.DefenseState(Player,Tick).Stamina;
        public Fight(bool sword=true, bool profession=true, ushort selected=20)
        {
            var catalog=ContentCatalogTests.Load(); var grid=new NavigationOptions().CreateGrid(new MovementOptions().ToSettings());
            Combat=new(catalog,Spatial,grid,new CombatOptions(),20,8,()=>1);
            Combat.Add(Player,Vector2.Zero,CombatEntityKind.Player); Spatial.Add(Player,Vector2.Zero);
            AddTarget(Target,new(0,1.5f));
            Abilities=new(catalog,Combat,Spatial,grid,new CombatOptions(),8,(_,_,_)=>true);
            Abilities.AddPlayer(Player,catalog.Creatures["test_adventurer"]);
            Inventory=new(catalog,Combat,Abilities,_=>catalog.Creatures["test_adventurer"].Stats);
            Inventory.Add(Player,new SavedInventory { Items=[new(Guid.NewGuid(),"arena_training_sword",sword?EquipmentSlot.Weapon:EquipmentSlot.None)] });
            Combat.SwordEquipped=Inventory.HasEquippedSword; Combat.WeaponUsable=Inventory.WeaponUsable;
            Combat.SetProfession(Player,profession?(ushort)2:(ushort)0);
            var saved=SavedProgression.Starter(catalog.Creatures["test_adventurer"],catalog);
            var skills=catalog.Professions.Single(p=>p.Id==2).AllSkills.Select(id=>new SavedSkill(id,1,0,catalog.Abilities[id].NetworkId==selected?(byte)1:(byte)0));
            Abilities.ApplyProgression(Player,saved with { Skills=[..saved.Skills.Select(s=>s with { Slot=0 }),..skills] });
        }
        public void AddTarget(NetworkEntityId id,Vector2 at,bool flesh=true)
        {
            Combat.Add(id,at,CombatEntityKind.Monster); Spatial.Add(id,at);
            var actor=Combat.Get(id); actor.Stats=actor.Stats with {MaxHealth=10000,PhysicalDefense=0}; actor.Health=10000; actor.CanBleed=flesh;
        }
        public void Position(NetworkEntityId id,Vector2 at) { Combat.Move(id,at); Spatial.Move(id,at); }
        public void Step(int count=1)
        { for(var i=0;i<count;i++) { Combat.Simulate(.05f,++Tick); Abilities.Simulate(.05f,Tick); Hits.AddRange(Abilities.Hits); } }
        public AbilityOutcome Cast(ushort skill,Vector2? direction=null,float distance=0)
        {
            Assert.True(Abilities.Queue(Player,new(++AbilitySequence,0,skill,direction??Vector2.UnitY,distance),Tick,0)); Step();
            return Abilities.Results[Player].Outcome;
        }
        public void Guard(DefenseAction action)
        { Assert.True(Combat.QueueDefense(Player,new(++DefenseSequence,action,Vector2.UnitY),Tick)); Step(); }
        public AttackEvent Swing(Vector2? direction=null)
        {
            Assert.True(Combat.Queue(Player,new(++AttackSequence,0,direction??Vector2.UnitY),Tick)); Step();
            return Assert.Single(Combat.Events);
        }
        public void ParryActualHit()
        {
            Guard(DefenseAction.Parry);
            Assert.True(Combat.ExecuteNpcAttack(Target,1,-Vector2.UnitY,Tick));
            Assert.Equal(GuardImpact.Parried,Combat.Events[^1].Guard);
        }
    }

    [Theory]
    [InlineData(20,1.2,8)] [InlineData(21,1,14)] [InlineData(22,.8,14)]
    [InlineData(23,1.7,20)] [InlineData(24,.4,12)] [InlineData(25,.7,12)]
    [InlineData(27,1.2,24)] [InlineData(29,1.5,22)]
    public void DirectSkillsUseSwordBaseNotMasteryBonusAndSpendOnce(int id,double factor,double stamina)
    {
        var f=new Fight(selected:(ushort)id); f.Combat.Get(f.Target).CanBleed=false;
        if (id==24) f.Position(f.Target,new(0,1));
        var power=f.Combat.SwordPower(f.Player);
        Assert.Equal(AbilityOutcome.Accepted,f.Cast((ushort)id)); Assert.Equal(100-stamina,f.Stamina,6);
        f.Step(16); Assert.Equal(power*factor,f.Damage,6); Assert.Single(f.Hits);
        Assert.Equal(AbilityOutcome.Cooldown,f.Cast((ushort)id)); Assert.Equal(power*factor,f.Damage,6);
    }

    [Fact]
    public void NoSwordNoStaminaAndParryAttemptWithoutHitCannotUseSkills()
    {
        var unarmed=new Fight(sword:false); Assert.Equal(AbilityOutcome.NeedsSword,unarmed.Cast(20)); Assert.Equal(100,unarmed.Stamina);
        var f=new Fight(); f.Combat.RestoreDefense(f.Player,new(1,7,0,1));
        Assert.Equal(AbilityOutcome.NoStamina,f.Cast(20)); Assert.Equal(7,f.Stamina);
        var counter=new Fight(selected:26); counter.Guard(DefenseAction.Parry);
        Assert.Equal(AbilityOutcome.NeedsParry,counter.Cast(26));
    }

    [Fact]
    public void ThrustHitsOnlyFirstNarrowTargetAtExtendedReachWithoutMoving()
    {
        var f=new Fight(); f.Position(f.Target,new(0,2.5f)); f.AddTarget(new(3),new(0,2.65f)); f.AddTarget(new(4),new(1,1));
        Assert.Equal(AbilityOutcome.Accepted,f.Cast(20)); f.Step(3);
        Assert.Equal(f.Target,Assert.Single(f.Hits).TargetId); Assert.Equal(Vector2.Zero,f.Combat.Get(f.Player).Position);
    }

    [Theory]
    [InlineData(21,2)] [InlineData(27,3)]
    public void FrontArcAndCircleHaveDifferentShapesAndDirectionDoesNotTrack(int skill,int hits)
    {
        var f=new Fight(selected:(ushort)skill); f.AddTarget(new(3),new(1,1)); f.AddTarget(new(4),new(0,-1));
        f.Cast((ushort)skill); f.Step(15); Assert.Equal(hits,f.Hits.Count); Assert.Equal(hits,f.Hits.Select(h=>h.TargetId).Distinct().Count());
    }

    [Fact]
    public void RendDealsBleedOnlyToFleshAndDoesNotRefreshFootwork()
    {
        var f=new Fight(selected:22); var power=f.Combat.SwordPower(f.Player);
        f.Cast(22); f.Step(100);
        Assert.Equal(power*1.4,f.Damage,5); Assert.True(f.Hits.Count>1); Assert.Equal(1,f.Combat.OrdinaryMovement(f.Player));
        var stone=new Fight(selected:22); stone.Combat.Get(stone.Target).CanBleed=false; stone.Cast(22); stone.Step(100);
        Assert.Single(stone.Hits); Assert.Equal(power*.8,stone.Damage,5);
    }

    [Theory]
    [InlineData(DefenseAction.Block,GuardImpact.Blocked)]
    [InlineData(DefenseAction.Parry,GuardImpact.Parried)]
    public void SwordSkillsRespectDirectionalPlayerDefense(DefenseAction defense,GuardImpact expected)
    {
        var f=new Fight();
        f.Combat.Remove(f.Target); f.Spatial.Remove(f.Target);
        f.Combat.Add(f.Target,new(0,1),CombatEntityKind.Player); f.Spatial.Add(f.Target,new(0,1));
        f.Combat.DamagePermission=(_,_)=>true;
        var victim=f.Combat.Get(f.Target); var before=victim.Health;
        Assert.True(f.Combat.QueueDefense(f.Target,new(1,defense,-Vector2.UnitY),f.Tick));
        f.Step(); f.Cast(20); f.Step(3);
        var hit=Assert.Single(f.Hits); Assert.Equal(expected,hit.Guard);
        if(defense==DefenseAction.Parry) { Assert.Equal(0,hit.Damage); Assert.Equal(before,victim.Health); }
        else Assert.InRange(hit.Damage,.01,f.Combat.SwordPower(f.Player)*1.2);
    }

    [Fact]
    public void BleedingDamageDoesNotCancelRecoveryButDirectDamageDoes()
    {
        var f=new Fight(selected:28); f.Combat.RestoreDefense(f.Player,new(1,30,0,1));
        f.Cast(28); f.Step(8);
        f.Combat.ApplyBleedDamage(f.Target,f.Player,1); f.Step();
        Assert.True(f.Combat.Get(f.Player).IsCasting);
        f.Step(16); Assert.InRange(f.Stamina,55,57);
        var direct=new Fight(selected:28); direct.Cast(28); direct.Step(8);
        direct.Combat.ApplyAbilityDamage(direct.Player,1,direct.Target); direct.Step();
        Assert.False(direct.Combat.Get(direct.Player).IsCasting);
    }

    [Theory]
    [InlineData(100,1.7/1.7)] [InlineData(-50,1.7*1.5)]
    public void BreakerIgnoresOnlyPositiveArmor(double armor,double expectedFactor)
    {
        var f=new Fight(selected:23); var target=f.Combat.Get(f.Target); target.Stats=target.Stats with {PhysicalDefense=armor};
        f.Cast(23); f.Step(20); Assert.Equal(f.Combat.SwordPower(f.Player)*expectedFactor,f.Damage,5);
    }

    [Fact]
    public void PommelStunsAndInterruptsButCannotStunImmuneTargets()
    {
        var f=new Fight(selected:24); f.Position(f.Target,new(0,1)); var interrupted=false;
        f.Combat.InterruptRequested+=id=>interrupted=id==f.Target;
        f.Cast(24); f.Step(3); Assert.True(interrupted); Assert.True(f.Combat.IsStunned(f.Target));
        Assert.False(f.Combat.ExecuteNpcAttack(f.Target,1,-Vector2.UnitY,f.Tick)); f.Step(10); Assert.False(f.Combat.IsStunned(f.Target));
        var immune=new Fight(selected:24); immune.Position(immune.Target,new(0,1)); immune.Combat.Get(immune.Target).CanBeStunned=false;
        immune.Cast(24); immune.Step(3); Assert.False(immune.Combat.IsStunned(immune.Target)); Assert.True(immune.Damage>0);
    }

    [Fact]
    public void SlowExpiresAndDoesNotDisableActions()
    {
        var f=new Fight(selected:25); f.Cast(25); f.Step(5);
        Assert.Equal(.75f,f.Combat.OrdinaryMovement(f.Target)); Assert.False(f.Combat.IsStunned(f.Target));
        Assert.True(f.Combat.ExecuteNpcAttack(f.Target,1,-Vector2.UnitY,f.Tick)); f.Step(42); Assert.Equal(1,f.Combat.OrdinaryMovement(f.Target));
    }

    [Fact]
    public void RealParryOpensRiposteAndFocusAndMissConsumesBoth()
    {
        var f=new Fight(selected:26); f.ParryActualHit(); var basePower=f.Combat.SwordPower(f.Player);
        f.Cast(26); f.Step(3); Assert.Equal(basePower*1.7*1.1,f.Damage,6); Assert.False(f.Combat.HasRiposte(f.Player)); Assert.Equal(1,f.Combat.ConsumeFocus(f.Player));
        var miss=new Fight(selected:26); miss.ParryActualHit(); Assert.Equal(AbilityOutcome.Accepted,miss.Cast(26,-Vector2.UnitY)); miss.Step(3);
        Assert.Equal(0,miss.Damage); Assert.False(miss.Combat.HasRiposte(miss.Player)); Assert.Equal(1,miss.Combat.ConsumeFocus(miss.Player));
        var expired=new Fight(selected:26); expired.ParryActualHit(); expired.Step(65); Assert.Equal(AbilityOutcome.NeedsParry,expired.Cast(26));
    }

    [Theory]
    [InlineData("damage")] [InlineData("move")] [InlineData("attack")] [InlineData("defense")]
    public void BreathIsCancelledWithoutRefundOrRestoration(string cancellation)
    {
        var f=new Fight(selected:28); f.Combat.RestoreDefense(f.Player,new(1,30,0,1));
        f.Cast(28); f.Step(8);
        switch(cancellation)
        {
            case "damage": f.Combat.ApplyAbilityDamage(f.Player,1,f.Target); break;
            case "move": f.Abilities.CancelRecovery(f.Player); break;
            case "attack": f.Combat.Queue(f.Player,new(++f.AttackSequence,0,Vector2.UnitY),f.Tick); break;
            case "defense": f.Combat.QueueDefense(f.Player,new(++f.DefenseSequence,DefenseAction.Block,Vector2.UnitY),f.Tick); break;
        }
        f.Step(17); Assert.InRange(f.Stamina,30,35); Assert.False(f.Combat.Get(f.Player).IsCasting);
        Assert.Equal(AbilityOutcome.Cooldown,f.Cast(28));
    }

    [Fact]
    public void BreathCompletesAfterChannelWithoutNaturalRegenDuringIt()
    {
        var f=new Fight(selected:28); f.Combat.RestoreDefense(f.Player,new(1,30,0,1)); f.Cast(28); f.Step(22);
        Assert.Equal(30,f.Stamina); Assert.True(f.Combat.IsRooted(f.Player)); f.Step(3); Assert.InRange(f.Stamina,55,56);
    }

    [Theory]
    [InlineData(3000,2.3)] [InlineData(3001,1.5)]
    public void FinisherChecksHealthAtImpactNotAtActivation(double health,double factor)
    {
        var f=new Fight(selected:29); f.Cast(29); f.Combat.Get(f.Target).Health=health; f.Step(16);
        Assert.Equal(f.Combat.SwordPower(f.Player)*factor,health-f.Combat.Get(f.Target).Health,6);
        Assert.Equal(AbilityOutcome.Cooldown,f.Cast(29));
    }

    [Fact]
    public void BasicMasteryRhythmFootworkAndTimeoutHaveIndependentEffects()
    {
        var f=new Fight(); f.Combat.RestoreDefense(f.Player,new(1,40,0,60));
        var first=f.Swing(); Assert.Equal(f.Combat.SwordPower(f.Player)*1.1,first.Damage,6); Assert.Equal(1.1f,f.Combat.OrdinaryMovement(f.Player));
        f.Step(21); f.Swing(); f.Step(21); f.Swing(); Assert.Equal(46,f.Stamina);
        f.Step(21); f.Swing(-Vector2.UnitY); f.Step(21); f.Swing(); f.Step(21); f.Swing(); Assert.Equal(46,f.Stamina);
        f.Step(82); f.Swing(); Assert.Equal(46,f.Stamina);
        f.Combat.SetProfession(f.Player,0); Assert.Equal(1,f.Combat.OrdinaryMovement(f.Player));
    }

    [Fact]
    public void StrongGripReducesRemainderNotAbsorptionAndRegenPausesInBlock()
    {
        var plain=new Fight(profession:false); plain.Guard(DefenseAction.Block); plain.Combat.ExecuteNpcAttack(plain.Target,1,-Vector2.UnitY,plain.Tick); var baseline=plain.Combat.Events[^1].Damage;
        var sword=new Fight(); sword.Guard(DefenseAction.Block); sword.Combat.ExecuteNpcAttack(sword.Target,1,-Vector2.UnitY,sword.Tick);
        Assert.Equal(baseline*.85,sword.Combat.Events[^1].Damage,6);
        sword.Combat.RestoreDefense(sword.Player,new(1,50,0,0));
        for(var i=0;i<40;i++) { sword.Guard(DefenseAction.Block); }
        Assert.Equal(50,sword.Stamina); sword.Guard(DefenseAction.Release); sword.Step(20); Assert.InRange(sword.Stamina,58,59);
    }

    [Fact]
    public void LongDashUsesOwnProfileFullCostForShortDistanceAndCannotBreakStun()
    {
        var f=new Fight(); var dash=f.Abilities.Loadout(f.Player,0).Abilities.Single(a=>a.Form==AbilityForm.Dash);
        Assert.Equal(20,dash.Range); Assert.Equal(35,dash.StaminaCost); Assert.Equal(20,dash.CooldownSeconds);
        Assert.Equal(AbilityOutcome.Accepted,f.Cast(3,Vector2.UnitX,1)); Assert.Equal(65,f.Stamina);
        f.Step(4); Assert.Equal(AbilityOutcome.Cooldown,f.Cast(3));
        var stunned=new Fight(); stunned.Combat.Stun(stunned.Player,.4); Assert.Equal(AbilityOutcome.InvalidState,stunned.Cast(3)); Assert.Equal(100,stunned.Stamina);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void OrdinaryDashRemainsThreeMetersWithoutSwordPassives(bool sword, bool profession)
    {
        var f = new Fight(sword: sword, profession: profession);
        f.Position(f.Player, new(-10, 0));
        var dash = f.Abilities.Loadout(f.Player, 0).Abilities.Single(a => a.Form == AbilityForm.Dash);
        Assert.Equal(3, dash.Range);
        Assert.Equal(20, dash.StaminaCost);
        Assert.Equal(3, dash.CooldownSeconds);
        Assert.Equal(AbilityOutcome.InvalidAim, f.Cast(3, Vector2.UnitX, 3.01f));
        Assert.Equal(AbilityOutcome.InvalidAim, f.Cast(3, Vector2.UnitX, 20));
        Assert.Equal(100, f.Stamina);
        Assert.Equal(AbilityOutcome.Accepted, f.Cast(3, Vector2.UnitX, 3));
        Assert.Equal(80, f.Stamina);
        var effect = Assert.Single(f.Abilities.ActiveStates);
        Assert.Equal(3, Vector2.Distance(effect.Origin, effect.Position), 5);
        f.Step(8);
        Assert.Equal(AbilityOutcome.Cooldown, f.Cast(3));
    }

    [Fact]
    public void EverySwordsmanSkillPublishesItsExecutionGeometry()
    {
        var catalog = ContentCatalogTests.Load();
        for (ushort id = 20; id <= 29; id++)
        {
            var f = new Fight(selected: id);
            var profile = f.Abilities.Loadout(f.Player, 0).Abilities.Single(a => a.Id == id);
            var technique = catalog.Abilities.Values.Single(a => a.NetworkId == id).Melee!;
            Assert.True(profile.Area.IsValid);
            Assert.Equal(technique.Stationary, profile.Area.Stationary);
            if (profile.Form == AbilityForm.Recovery) Assert.Equal(AbilityAreaShape.Self, profile.Area.Shape);
            else
            {
                Assert.Equal(profile.Range, profile.Area.Length);
                Assert.Equal((float)f.Combat.Get(f.Player).Weapon.Range * technique.RangeFactor, profile.Area.Length);
                Assert.Equal(technique.NarrowThrust ? AbilityAreaShape.Corridor : AbilityAreaShape.Sector, profile.Area.Shape);
                if (!technique.NarrowThrust) Assert.Equal(technique.ArcDegrees * MathF.PI / 360, profile.Area.HalfAngleRadians);
            }
        }
        var dash = new Fight().Abilities.Loadout(new(1), 0).Abilities.Single(a => a.Form == AbilityForm.Dash);
        Assert.Equal(new AbilityArea(AbilityAreaShape.Corridor, 20, .45f), dash.Area);
    }

    [Fact]
    public void SwordDashBecomesAvailableAfterTwentySeconds()
    {
        var f = new Fight();
        Assert.Equal(AbilityOutcome.Accepted, f.Cast(3, Vector2.UnitX, 1));
        f.Step(378); // 18.9 seconds after the accepted cast.
        Assert.Equal(AbilityOutcome.Cooldown, f.Cast(3, Vector2.UnitX, 1));
        f.Step(21);
        Assert.Equal(AbilityOutcome.Accepted, f.Cast(3, Vector2.UnitX, 1));
    }

    [Fact]
    public void TwentyMeterSwordDashRejectsLargerIntentsAndKeepsCooldownAndCost()
    {
        var f=new Fight(); f.Position(f.Player,new(-10,0));
        Assert.Equal(AbilityOutcome.InvalidAim,f.Cast(3,Vector2.UnitX,20.01f)); Assert.Equal(100,f.Stamina);
        Assert.Equal(AbilityOutcome.Accepted,f.Cast(3,Vector2.UnitX,20)); Assert.Equal(65,f.Stamina);
        var effect=Assert.Single(f.Abilities.ActiveStates);
        Assert.Equal(20,Vector2.Distance(effect.Origin,effect.Position),5);
        Assert.Equal(AbilityOutcome.Busy,f.Cast(3)); f.Step(30);
        Assert.Equal(AbilityOutcome.Cooldown,f.Cast(3));
        Assert.Equal(20,f.Abilities.Loadout(f.Player,f.Tick).Abilities.Single(a=>a.Form==AbilityForm.Dash).CooldownSeconds);
    }
}
