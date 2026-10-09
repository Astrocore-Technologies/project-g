using System.Numerics;
using Content.Shared.Network;
using Xunit;
namespace Content.Tests.Server;
public sealed class SocialCombatTests
{
    [Theory][InlineData(1)][InlineData(2)]public void PartyBlocksRealProjectileAndAreaDamageAndKeepsPersonalExp(ushort skill)
    {
        var w=PvpTests.World();w.EnableSocial([]);var a=w.AddPlayer(42,new(1),PvpTests.Initial(w));
        // Full health makes unchanged HP a friendly-fire assertion, independent of passive recovery.
        var b=w.AddPlayer(43,new(2),PvpTests.Initial(w,-5,-4) with { Health=w.CreateInitialCharacter().Health });var ai=Guid.NewGuid();var bi=Guid.NewGuid();w.BindWorldActor(42,ai);w.BindWorldActor(43,bi);w.BindSocial(42,ai);w.BindSocial(43,bi);var s=w.Social!;
        SocialTests.Command(s,ai,SocialAction.Create);SocialTests.Command(s,ai,SocialAction.Invite,target:bi);SocialTests.Command(s,bi,SocialAction.Accept,invite:Assert.Single(s.Invitations(bi)));
        PvpTests.Command(w,42,1,PvpMode.Criminal);PvpTests.Command(w,43,1,PvpMode.Voluntary);var hp=w.Combat!.Get(b.EntityId).Health;var xp=w.CaptureCharacter(42).Progression!.Experience;
        Assert.True(w.TryQueueAbility(42,new(1,w.Tick,skill,skill==1?Vector2.UnitY:b.Position),0));for(var i=0;i<80;i++)w.Simulate(.05f);
        Assert.Equal(hp,w.Combat.Get(b.EntityId).Health);Assert.Equal(0,w.Combat.ApplyEchoDamage(b.EntityId,100,a.EntityId,a.Position));Assert.Equal(0,w.Combat.ApplyAbilityDamage(b.EntityId,100,a.EntityId));Assert.Equal(xp,w.CaptureCharacter(42).Progression!.Experience);Assert.Equal(0,w.PrivatePvp(a.EntityId).Pk);Assert.Equal(0,w.PrivatePvp(a.EntityId).Reputation);
        Assert.True(w.Combat.ApplyAbilityDamage(b.EntityId,1,w.Npc!.Id)>0);
    }
    [Fact]public void GuildAloneDoesNotBlockPlayerDamage()
    {
        var w=PvpTests.World();w.EnableSocial([]);var a=w.AddPlayer(42,new(1),PvpTests.Initial(w));var b=w.AddPlayer(43,new(2),PvpTests.Initial(w,-4.5f));var ai=Guid.NewGuid();var bi=Guid.NewGuid();w.BindWorldActor(42,ai);w.BindWorldActor(43,bi);w.BindSocial(42,ai);w.BindSocial(43,bi);var s=w.Social!;SocialTests.Command(s,ai,SocialAction.Create,SocialKind.Guild,name:"Guild");SocialTests.Command(s,ai,SocialAction.Invite,SocialKind.Guild,bi);SocialTests.Command(s,bi,SocialAction.Accept,SocialKind.Guild,invite:Assert.Single(s.Invitations(bi)));PvpTests.Command(w,42,1,PvpMode.Criminal);Assert.True(w.Combat!.ApplyAbilityDamage(b.EntityId,1,a.EntityId)>0);
    }
}
