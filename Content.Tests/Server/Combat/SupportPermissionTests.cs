using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;
namespace Content.Tests.Server.Combat;

public sealed class SupportPermissionTests
{
    [Fact]public void WorldPartyControlsHealShieldAndResurrectionAndExplicitSkillPermissionAllowsOthers()
    {
        var json=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        json["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"party_hot","durationSeconds":4,"periodic":{"kind":"Healing","intervalSeconds":1,"power":{"flat":5}}}"""));
        json["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"party_shield","durationSeconds":4,"shield":{"power":{"flat":10}}}"""));
        var world=PvpTests.World(ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString())));world.EnableSocial([],64,32);
        var source=world.AddPlayer(42,new(1),PvpTests.Initial(world));var target=world.AddPlayer(43,new(2),PvpTests.Initial(world,-4.5f));
        var sourceIdentity=Guid.NewGuid();var targetIdentity=Guid.NewGuid();world.BindWorldActor(42,sourceIdentity);world.BindWorldActor(43,targetIdentity);
        world.BindSocial(42,sourceIdentity);world.BindSocial(43,targetIdentity);var combat=world.Combat!;
        Assert.True(combat.Heal(source.EntityId,source.EntityId,1)>0);
        Assert.Equal(0,combat.Heal(source.EntityId,target.EntityId,1));Assert.False(combat.ApplyEffect(source.EntityId,target.EntityId,"party_shield"));
        var social=world.Social!;Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(social,sourceIdentity,SocialAction.Create));
        Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(social,sourceIdentity,SocialAction.Invite,target:targetIdentity));
        Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(social,targetIdentity,SocialAction.Accept,invite:Assert.Single(social.Invitations(targetIdentity))));
        Assert.Equal(1,combat.Heal(source.EntityId,target.EntityId,1));Assert.True(combat.ApplyEffect(source.EntityId,target.EntityId,"party_shield"));
        Assert.True(combat.ApplyEffect(source.EntityId,target.EntityId,"party_hot"));var hp=combat.Get(target.EntityId).Health;
        Assert.Equal(SocialOutcome.Accepted,SocialTests.Command(social,targetIdentity,SocialAction.Leave));
        Assert.Equal(0,combat.Heal(source.EntityId,target.EntityId,1));world.Simulate(1);
        Assert.True(combat.Get(target.EntityId).Health>=hp+5); // The applied HoT retains its authorization.
        Assert.True(combat.ApplyEffect(source.EntityId,target.EntityId,"party_shield",allowOtherPlayers:true));
        Assert.Equal(1,combat.Heal(source.EntityId,target.EntityId,1,allowOtherPlayers:true));
        Assert.True(combat.PayHealth(target.EntityId,combat.Get(target.EntityId).Health,true));var death=combat.CaptureLife(target.EntityId).DeathId;
        var legalDeath=world.PrivatePvp(target.EntityId);Assert.True(legalDeath.Dead);
        Assert.False(combat.Resurrect(source.EntityId,target.EntityId,death,20));
        Assert.True(combat.Resurrect(source.EntityId,target.EntityId,death,20,allowOtherPlayers:true));
        Assert.Equal(20,combat.Get(target.EntityId).Health);Assert.False(world.PrivatePvp(target.EntityId).Dead);
        world.Simulate(.05f);Assert.False(world.PrivatePvp(target.EntityId).Dead);Assert.Equal(legalDeath.DeathId,world.PrivatePvp(target.EntityId).DeathId);
    }
}
