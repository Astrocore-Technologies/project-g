using System.Numerics;
using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CompanionPvpTests
{
    private static NetworkEntityId Echo(ServerWorld world,NetworkEntityId owner,uint runtime=100,Vector2? position=null)
    {
        var id=new NetworkEntityId(runtime);world.Combat!.Add(id,position??world.Combat.Get(owner).Position,CombatEntityKind.Echo,"mira_combat");
        world.Combat.BindCompanion(id,owner,Guid.NewGuid());return id;
    }

    [Fact]
    public void EchoInheritsOwnersPvpModeAndRemainsAnIndependentNpcTarget()
    {
        var world=PvpTests.World();var a=world.AddPlayer(42,new(1),PvpTests.Initial(world));
        var b=world.AddPlayer(43,new(2),PvpTests.Initial(world,-4.5f));var echo=Echo(world,a.EntityId);
        Assert.False(world.Combat!.CanTarget(echo,b.EntityId));Assert.False(world.Combat.CanTarget(b.EntityId,echo));
        Assert.True(world.Combat.CanTarget(world.Npc!.Id,echo));Assert.True(world.Combat.CanTarget(echo,world.Npc.Id));
        Assert.False(world.Combat.CanTarget(echo,a.EntityId));Assert.False(world.Combat.CanTarget(a.EntityId,echo));
        Assert.Equal(PvpOutcome.Accepted,PvpTests.Command(world,42,1,PvpMode.Criminal));
        Assert.True(world.Combat.CanTarget(echo,b.EntityId));
        Assert.True(world.Combat.ApplyAbilityDamage(b.EntityId,1,echo)>0);
        Assert.Equal(-10,world.PrivatePvp(a.EntityId).Reputation);Assert.True(world.Combat.InCombat(a.EntityId));
        Assert.True(world.Combat.CanTarget(b.EntityId,echo));
    }

    [Fact]
    public void EchoDeathNeverKillsOrDropsTheOwnersInventoryOrProgress()
    {
        var world=PvpTests.World();var a=world.AddPlayer(42,new(1),PvpTests.Initial(world));
        var b=world.AddPlayer(43,new(2),PvpTests.Initial(world,-4.5f));var echo=Echo(world,b.EntityId);
        var before=world.CaptureCharacter(43);PvpTests.Command(world,42,1,PvpMode.Criminal);
        Assert.True(world.Combat!.ApplyAbilityDamage(echo,100000,a.EntityId)>0);world.Simulate(.05f);
        Assert.True(world.Combat.CaptureLife(echo).Dead);Assert.False(world.PrivatePvp(b.EntityId).Dead);
        var owner=world.Combat.Get(b.EntityId);
        Assert.Equal(before.Health+owner.Stats.HealthRecovery*(double).05f*2,owner.Health,8);
        Assert.Equal(before.Progression!.Experience,world.CaptureCharacter(43).Progression!.Experience);
        Assert.Equal(before.Inventory!.Serialize(),world.CaptureCharacter(43).Inventory!.Serialize());
        Assert.Equal(0,world.PrivatePvp(a.EntityId).Pk);Assert.Empty(world.CaptureWorldNode().DeathLoot??[]);
        Assert.True(world.Combat.InCombat(b.EntityId));
    }

    [Fact]
    public void ProtectionAndSafeOwnerOrBodyPreventPvpDamage()
    {
        var world=PvpTests.World();world.PvpClock=()=>100000;
        var a=world.AddPlayer(42,new(1),PvpTests.Initial(world));var initial=PvpTests.Initial(world,-4.5f);
        var b=world.AddPlayer(43,new(2),initial with {Progression=initial.Progression! with {Pvp=new(){ProtectionUntil=110000}}});
        var protectedEcho=Echo(world,b.EntityId);PvpTests.Command(world,42,1,PvpMode.Criminal);
        Assert.False(world.Combat!.CanTarget(a.EntityId,protectedEcho));
        var safe=world.AddPlayer(44,new(3),PvpTests.Initial(world,-9,0));var outsideEcho=Echo(world,safe.EntityId,101,new(-4.5f,-5));
        Assert.False(world.Combat.CanTarget(a.EntityId,outsideEcho));
        var insideEcho=Echo(world,a.EntityId,102,new(-9,0));Assert.False(world.Combat.CanTarget(insideEcho,b.EntityId));
    }

    [Fact]
    public void PartyMembersCannotDamageEachOthersEchoesEvenInCriminalMode()
    {
        var world=PvpTests.World();world.EnableSocial([]);
        var a=world.AddPlayer(42,new(1),PvpTests.Initial(world));var b=world.AddPlayer(43,new(2),PvpTests.Initial(world,-4.5f));
        var first=world.SocialCharacter(42);var second=world.SocialCharacter(43);
        world.Social!.SetOnline(first);world.Social.SetOnline(second);
        Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(world.Social,first,SocialAction.Create));
        Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(world.Social,first,SocialAction.Invite,target:second));
        Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(world.Social,second,SocialAction.Accept,invite:Assert.Single(world.Social.Invitations(second))));
        var one=Echo(world,a.EntityId);var two=Echo(world,b.EntityId,101);
        PvpTests.Command(world,42,1,PvpMode.Criminal);
        Assert.False(world.Combat!.CanTarget(one,two));Assert.False(world.Combat.CanTarget(a.EntityId,two));
        Assert.False(world.Combat.CanTarget(one,b.EntityId));
    }

    [Fact]
    public void ReleasedEchoProjectileKeepsOwnerAffiliationAndTagsLiveOwnerAfterBodyRemoval()
    {
        var world=PvpTests.World();var a=world.AddPlayer(42,new(1),PvpTests.Initial(world));
        var b=world.AddPlayer(43,new(2),PvpTests.Initial(world,-4.5f));var echo=Echo(world,a.EntityId);
        PvpTests.Command(world,42,1,PvpMode.Criminal);
        var combat=world.Combat!;var released=combat.CaptureReleasedSource(echo,1);var hit=combat.CaptureDirectHit(echo,1);
        Assert.Equal(world.SocialCharacter(42),released.Pvp!.Character);combat.Remove(echo);
        Assert.True(combat.CanReleasedTarget(released,b.EntityId));Assert.False(combat.CanReleasedTarget(released,a.EntityId));
        var damage=combat.ResolveDirectHit(echo,b.EntityId,Vector2.UnitX,1,false,snapshot:hit,releasedSource:released);
        Assert.True(damage.Damage>0);Assert.True(combat.InCombat(a.EntityId));
        Assert.Equal(-10,world.PrivatePvp(a.EntityId).Reputation);Assert.True(world.PublicPvp(a.EntityId).Aggressor);
    }

    [Fact]
    public void ExistingEchoDotKeepsPvpDeathAttributionAfterTheBodyIsRemoved()
    {
        var world=PvpTests.World();world.PvpClock=()=>100000;
        var a=world.AddPlayer(42,new(1),PvpTests.Initial(world));var b=world.AddPlayer(43,new(2),PvpTests.Initial(world,-4.5f));
        var echo=Echo(world,a.EntityId);PvpTests.Command(world,42,1,PvpMode.Criminal);
        Assert.True(world.Combat!.ApplyEffect(echo,b.EntityId,"sword_rend_bleed"));
        world.Combat.Get(b.EntityId).Health=1;world.Combat.Remove(echo);world.Simulate(1);
        Assert.True(world.PrivatePvp(b.EntityId).Dead);Assert.True(world.PrivatePvp(b.EntityId).PvpDeath);
        Assert.Equal(1,world.PrivatePvp(a.EntityId).Pk);Assert.True(world.Combat.InCombat(a.EntityId));
    }
}
