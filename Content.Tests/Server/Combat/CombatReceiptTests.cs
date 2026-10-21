using System.Numerics;
using Content.Server.Combat;
using Content.Server.Networking;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CombatReceiptTests
{
    [Fact]public void ReleaseCannotBeErasedByAnAimAtTheSameReceiptBoundary()
    {
        var f=new ChannelingTests.Fight(ChargedAbilityTests.Document());f.Cast();
        var aim=f.Combat.Get(f.World.TrainingTargetId).Position;var at=f.Combat.Time+.01;
        Assert.True(f.Combat.ScheduleInput(f.Id,new(CombatInputKind.SkillControl,
            SkillControl:new(1,1,0,AbilityControlKind.Release,aim)),at));
        Assert.True(f.Combat.ScheduleInput(f.Id,new(CombatInputKind.SkillControl,
            SkillControl:new(2,1,0,AbilityControlKind.Aim,aim+new Vector2(3,0))),at));
        f.Step();Assert.Single(f.Hits);Assert.Equal(98,f.Mana);Assert.Equal(0,f.Skill.Charges);
    }
    [Fact]public void AReceivedBasicCancelSurvivesALaterAimForTheSameAction()
    {
        var f=new BasicActionPhaseTests.Fight();f.Begin();var at=f.Combat.Time+.01;
        Assert.True(f.Combat.ScheduleInput(f.Player,new(CombatInputKind.BasicControl,
            BasicControl:new(1,1,0,BasicControlKind.Cancel,Vector2.UnitY)),at));
        Assert.True(f.Combat.ScheduleInput(f.Player,new(CombatInputKind.BasicControl,
            BasicControl:new(2,1,0,BasicControlKind.Aim,Vector2.UnitY)),at));
        f.Step();f.Step(.3f);Assert.Null(f.Actor.Action);Assert.Empty(f.Combat.Events);
    }
    private static BasicActionPhaseTests.Fight Fast()
    {
        var fight=new BasicActionPhaseTests.Fight();var actor=fight.Actor;
        fight.Combat.ApplyEquipment(fight.Player,(actor.BaseDerivedStats with {AttackSpeedMultiplier=actor.Weapon.AttackIntervalSeconds/.01},actor.Weapon,.01));
        return fight;
    }
    [Fact]public void ManualReceiptsAndContinuousInputHaveEqualContactsAboveWorldTickRate()
    {
        var manual=Fast();var held=Fast();
        for(var i=0;i<5;i++)Assert.True(manual.Combat.ScheduleInput(manual.Player,
            new(CombatInputKind.Basic,Attack:new((uint)i+1,0,Vector2.UnitY)),i*.01));
        Assert.True(held.Combat.ScheduleInput(held.Player,new(CombatInputKind.Basic,
            Attack:new(1,0,Vector2.UnitY,BasicAttackMode.BeginContinuous)),0));
        manual.Step();held.Step();
        Assert.Equal(5,manual.Combat.Events.Count);Assert.Equal(5,held.Combat.Events.Count);
        Assert.Equal(manual.Combat.Events.Sum(e=>e.Damage),held.Combat.Events.Sum(e=>e.Damage),8);
    }
    [Fact]public void ManyEarlyReceiptsStillLeaveOnlyOneWaitingCommand()
    {
        var f=new BasicActionPhaseTests.Fight();
        for(var i=0;i<64;i++)Assert.True(f.Combat.ScheduleInput(f.Player,
            new(CombatInputKind.Basic,Attack:new((uint)i+1,0,Vector2.UnitY)),i*.00001));
        f.Step();Assert.Equal(1,f.Combat.Inputs.WaitingCount);
        f.Step(.3f);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
        Assert.Single(f.Combat.Events);Assert.Equal(64U,f.Combat.Get(f.Player).LastSequence);
    }
    [Fact]public void CapacityPreservesNewestTerminalControlsAndTheirReceiptOrder()
    {
        var buffer=new CombatInputBuffer();var actor=new NetworkEntityId(1);
        for(var i=0;i<64;i++)Assert.True(buffer.Add(new(actor,"test",new(CombatInputKind.Basic,Attack:new((uint)i+1,0,Vector2.UnitY)),0)));
        Assert.False(buffer.Add(new(actor,"test",new(CombatInputKind.Basic,Attack:new(65,0,Vector2.UnitY)),0)));
        Assert.True(buffer.Add(new(actor,"test",new(CombatInputKind.Basic,Attack:new(66,0,Vector2.UnitY,BasicAttackMode.EndContinuous)),0)));
        Assert.True(buffer.Add(new(actor,"test",new(CombatInputKind.Cancel,Cancel:new(1,1)),0)));
        Assert.True(buffer.Add(new(actor,"test",new(CombatInputKind.Basic,Attack:new(67,0,Vector2.UnitY,BasicAttackMode.EndContinuous)),0)));
        Assert.Equal(66,buffer.Events.Count);Assert.Equal(CombatInputKind.Cancel,buffer.Events[^2].Input.Kind);
        Assert.Equal(67U,buffer.Events[^1].Input.Attack.Sequence);
    }
}
