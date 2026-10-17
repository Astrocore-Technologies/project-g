using System.Numerics;
using Content.Server.Combat;
using Content.Shared.Combat;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class SimultaneousBasicTests
{
    private sealed class Pair
    {
        internal readonly BasicActionPhaseTests.Fight Fight=new();
        internal readonly NetworkEntityId Other=new(3);
        internal CombatSimulation Combat=>Fight.Combat;
        internal Combatant First=>Fight.Actor;
        internal Combatant Second=>Combat.Get(Other);
        internal Pair(double health=1)
        {
            Combat.Get(Fight.Target).Health=0;
            Combat.Add(Other,Vector2.UnitY,CombatEntityKind.Player);Fight.Spatial.Add(Other,Vector2.UnitY);
            Combat.ApplyEquipment(Other,(Second.BaseDerivedStats with {AttackSpeedMultiplier=1},Second.Weapon,1));
            First.Health=Second.Health=health;Combat.DamagePermission=(_,_)=>true;
        }
        internal void Queue(bool otherFirst=false)
        {
            if(otherFirst)Assert.True(Combat.Queue(Other,new(1,0,-Vector2.UnitY),Fight.Tick));
            Assert.True(Combat.Queue(First.Id,new(1,0,Vector2.UnitY),Fight.Tick));
            if(!otherFirst)Assert.True(Combat.Queue(Other,new(1,0,-Vector2.UnitY),Fight.Tick));
            Fight.Step();Assert.Empty(Combat.Events);
        }
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ExactlySimultaneousLethalContactsBothResolveInStableExecutionOrder(bool otherFirst)
    {
        var f=new Pair();f.Queue(otherFirst);f.Fight.Step(.21f);
        Assert.Equal(2,f.Combat.Events.Count);Assert.All(f.Combat.Events,h=>Assert.Equal(1,h.Damage));
        Assert.Equal(0,f.First.Health);Assert.Equal(0,f.Second.Health);Assert.True(f.First.LifeDead);Assert.True(f.Second.LifeDead);
        Assert.Null(f.First.Action);Assert.Null(f.Second.Action);
        Assert.True(f.Combat.Events[0].ExecutionId<f.Combat.Events[1].ExecutionId);
        Assert.Equal(2,f.Combat.Events.Select(h=>h.AttackerId).Distinct().Count());
    }

    [Fact]public void DifferentContactTimesInOneLargeStepDoNotBecomeASimultaneousTrade()
    {
        var f=new Pair();Assert.True(f.Combat.Queue(f.First.Id,new(1,0,Vector2.UnitY),0));f.Fight.Step();
        Assert.True(f.Combat.Queue(f.Other,new(1,0,-Vector2.UnitY),f.Fight.Tick));f.Fight.Step();
        Assert.True(f.First.Action!.PhaseEndsAt<f.Second.Action!.PhaseEndsAt);f.Fight.Step(.5f);
        Assert.Equal(f.First.Id,Assert.Single(f.Combat.Events).AttackerId);Assert.Equal(1,f.First.Health);Assert.Equal(0,f.Second.Health);
    }

    [Fact]public void ControlAndAccuracyLossFromTheFirstContactCannotRevokeTheSecondFixedContact()
    {
        var f=new Pair(1000);var count=0;
        f.Combat.DamageApplied=(source,target,_)=>
        {
            if(++count!=1)return;
            Assert.True(f.Combat.ApplyStatusControl(source,target,0,0,CrowdControlKind.Stun,1));
            var victim=f.Combat.Get(target);victim.Stats=victim.Stats with {Accuracy=-100000};
        };
        f.Queue();f.Fight.Step(.21f);
        Assert.Equal(2,f.Combat.Events.Count);Assert.All(f.Combat.Events,h=>{Assert.True(h.Damage>0);Assert.False(h.Evaded);});
        Assert.True(f.First.Health<1000);Assert.True(f.Second.Health<1000);
    }

    [Theory][InlineData(.21f)][InlineData(2f)]
    public void EveryFixedWeaponContactPrecedesReceiptCompletionAndDeathPublication(float delta)
    {
        var f=new Pair();var trace=new List<(char Kind,NetworkEntityId Actor)>();
        f.Combat.WeaponHealthDamage+=(actor,_)=>trace.Add(('H',actor));
        f.Combat.WeaponExecutionFinished+=(actor,_)=>trace.Add(('F',actor));
        f.Combat.Defeated+=actor=>trace.Add(('D',actor));
        f.Queue();f.Fight.Step(delta);
        Assert.Equal(2,trace.Count(e=>e.Kind=='H'));Assert.Equal(2,trace.Count(e=>e.Kind=='F'));Assert.Equal(2,trace.Count(e=>e.Kind=='D'));
        var lastDamage=trace.FindLastIndex(e=>e.Kind=='H');var firstFinish=trace.FindIndex(e=>e.Kind=='F');var firstDeath=trace.FindIndex(e=>e.Kind=='D');
        Assert.True(lastDamage<firstFinish);Assert.True(lastDamage<firstDeath);
        foreach(var actor in new[]{f.First.Id,f.Other})Assert.True(trace.FindIndex(e=>e.Kind=='F'&&e.Actor==actor)<trace.FindIndex(e=>e.Kind=='D'&&e.Actor==actor));
    }
}
