using System.Numerics;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.World;

public sealed class ThreatAiTests
{
    private sealed class Fight
    {
        internal readonly ServerWorld World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalogTests.Load(),npc:Options.Create(new NpcOptions{Enabled=true,X=-4,Z=0,AggroRadius=6,LeashRadius=10}),
            hitRoll:()=>0,criticalRoll:()=>1);
        internal NpcSimulation Npc=>World.Npc!;
        internal readonly ServerPlayer First,Second;
        internal Fight()
        {
            First=World.AddPlayer(1,new(1),World.CreateInitialCharacter() with {X=-5,Z=0});
            Second=World.AddPlayer(2,new(2),World.CreateInitialCharacter() with {X=-7,Z=0});
        }
        internal void Until(Func<bool> done)
        {for(var i=0;i<200&&!done();i++)World.Simulate(.05f);Assert.True(done(),"NPC did not reach its expected threat behavior.");}
        internal void Hit(ServerPlayer player,double power)=>World.Combat!.ResolveDirectHit(player.EntityId,Npc.Id,Vector2.UnitX,power,false);
    }

    [Fact]public void HigherThreatChangesTheNextTargetWithoutReversingAnAlreadyFixedClawStrike()
    {
        var f=new Fight();f.Until(()=>f.Npc.Behavior==NpcBehavior.Windup);Assert.Equal(f.First.EntityId,f.Npc.TargetId);
        var action=f.World.Combat!.Get(f.Npc.Id).Action!;var execution=action.ExecutionId;var direction=f.Npc.Telegraph!.Value.Direction;
        f.Hit(f.Second,10);f.World.Simulate(.05f);
        Assert.Equal(execution,f.World.Combat.Get(f.Npc.Id).Action!.ExecutionId);Assert.Equal(direction,f.Npc.Telegraph!.Value.Direction);
        f.Until(()=>f.Npc.TargetId==f.Second.EntityId);Assert.NotEqual(NpcBehavior.Returning,f.Npc.Behavior);
    }

    [Fact]public void TauntTemporarilyOverridesThreatThenTheHighestThreatReturns()
    {
        var f=new Fight();f.Hit(f.First,10);f.Hit(f.Second,20);f.Until(()=>f.Npc.TargetId==f.Second.EntityId);
        var before=f.World.Combat!.Encounter(f.Npc.Id).Threat.ToDictionary(p=>p.Key,p=>p.Value);
        Assert.True(f.World.Combat.ApplyStatusControl(f.First.EntityId,f.Npc.Id,1,1,CrowdControlKind.Taunt,1));
        f.Until(()=>f.Npc.TargetId==f.First.EntityId);Assert.Equal(before.OrderBy(p=>p.Key.Value),f.World.Combat.Encounter(f.Npc.Id).Threat.OrderBy(p=>p.Key.Value));
        f.Until(()=>!f.World.Combat.Get(f.Npc.Id).Controls.Has(CrowdControlKind.Taunt) && f.Npc.TargetId==f.Second.EntityId);
    }

    [Fact]public void LosingOneTargetKeepsAnotherValidOpponentAndReturnCannotBeCancelledByNewDamage()
    {
        var f=new Fight();f.Hit(f.First,20);f.Hit(f.Second,10);f.Until(()=>f.Npc.TargetId==f.First.EntityId);
        f.World.Combat!.PayHealth(f.First.EntityId,f.World.Combat.Get(f.First.EntityId).Health,true);
        f.Until(()=>f.Npc.TargetId==f.Second.EntityId);Assert.NotEqual(NpcBehavior.Returning,f.Npc.Behavior);
        f.World.Combat.Move(f.Second.EntityId,new(14,14));f.Until(()=>f.Npc.Behavior==NpcBehavior.Returning);
        var ledger=f.World.Combat.Encounter(f.Npc.Id);Assert.Empty(ledger.Threat);Assert.Empty(ledger.Contribution.Entries);
        var hp=f.World.Combat.Get(f.Npc.Id).Health;f.Hit(f.Second,1);
        Assert.True(f.World.Combat.Get(f.Npc.Id).Health<hp);Assert.Equal(NpcBehavior.Returning,f.Npc.Behavior);
        Assert.Empty(ledger.Threat);Assert.Empty(ledger.Contribution.Entries);
        // The actual navigation mover, rather than a synthetic position, completes the return.
        f.World.RemovePlayer(2);f.Until(()=>f.Npc.Behavior==NpcBehavior.Idle);
        Assert.Equal(f.Npc.Home,f.Npc.Motion.Position);
    }
}
