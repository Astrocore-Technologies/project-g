using Content.Server.Social;
using Content.Shared.Network;
using Xunit;
namespace Content.Tests.Server;
public sealed class SocialTests
{
    private long now=100000;
    private SocialSimulation New(Func<Guid,bool>? eligible=null)=>new(()=>now,eligible);
    private static Guid Player(SocialSimulation s){var c=Guid.NewGuid();s.SetOnline(c);return c;}
    internal static SocialOutcome Command(SocialSimulation s,Guid c,SocialAction action,SocialKind kind=SocialKind.Party,Guid target=default,byte capacity=0,string name="",SocialInvitation? invite=null)
    { var group=s.Group(c,kind);return s.Execute(c,new(s.Identity(c).LastOperation+1,kind,action,target==Guid.Empty?0:s.Identity(target).Handle,action==SocialAction.Create?0:invite?.Revision??group?.Revision??0,invite?.Token??0,capacity,action is SocialAction.Accept or SocialAction.Disband,name)); }
    private static void Join(SocialSimulation s,Guid a,Guid b,SocialKind kind=SocialKind.Party)
    { Assert.Equal(SocialOutcome.Accepted,Command(s,a,SocialAction.Invite,kind,b));Assert.Equal(SocialOutcome.Accepted,Command(s,b,SocialAction.Accept,kind,invite:Assert.Single(s.Invitations(b)))); }
    [Fact] public void SixTwentyCapacityRaceAndDowngrade()
    {
        var s=New();var a=Player(s);Command(s,a,SocialAction.Create);
        for(var i=0;i<4;i++)Join(s,a,Player(s));
        var b=Player(s);var c=Player(s);Command(s,a,SocialAction.Invite,target:b);Command(s,a,SocialAction.Invite,target:c);var invite=Assert.Single(s.Invitations(c));
        Assert.Equal(SocialOutcome.Accepted,Command(s,b,SocialAction.Accept,invite:Assert.Single(s.Invitations(b))));
        Assert.Equal(SocialOutcome.Missing,Command(s,c,SocialAction.Accept,invite:invite));Assert.Equal(6,s.Group(a,SocialKind.Party)!.Members.Length);
        Assert.Equal(SocialOutcome.Accepted,Command(s,a,SocialAction.Resize,capacity:20));
        for(var i=0;i<14;i++)Join(s,a,Player(s));
        Assert.Equal(SocialOutcome.Full,Command(s,a,SocialAction.Resize,capacity:6));Assert.Equal(SocialOutcome.Full,Command(s,a,SocialAction.Invite,target:Player(s)));
        Assert.Equal(20,s.Group(a,SocialKind.Party)!.Members.Length);
    }
    [Fact] public void OfflineLeaderTransfersAfterGraceAndRestartResetsGrace()
    {
        var s=New();var a=Player(s);var b=Player(s);Command(s,a,SocialAction.Create);Join(s,a,b);s.SetOffline(a);s.Advance();now+=119999;s.Advance();Assert.Equal(a,s.Group(b,SocialKind.Party)!.Leader);
        var rows=s.Dirty.ToArray();var restored=New();restored.Restore(rows);restored.SetOnline(b);restored.Advance();now+=1;restored.Advance();Assert.Equal(a,restored.Group(b,SocialKind.Party)!.Leader);
        now+=120000;restored.Advance();Assert.Equal(b,restored.Group(b,SocialKind.Party)!.Leader);restored.SetOnline(a);Assert.Equal(b,restored.Group(a,SocialKind.Party)!.Leader);
    }
    [Fact] public void BusyMembersAndOfflineTagsPreventMembershipChanges()
    {
        var blocked=new HashSet<Guid>();var s=New(c=>!blocked.Contains(c));var a=Player(s);var b=Player(s);Command(s,a,SocialAction.Create);Join(s,a,b);blocked.Add(b);
        Assert.Equal(SocialOutcome.Busy,Command(s,a,SocialAction.Kick,target:b));Assert.Equal(SocialOutcome.Busy,Command(s,a,SocialAction.Resize,capacity:20));blocked.Clear();s.SetOffline(b,now+120000);
        Assert.Equal(SocialOutcome.Busy,Command(s,a,SocialAction.Kick,target:b));now+=120000;Assert.Equal(SocialOutcome.Accepted,Command(s,a,SocialAction.Kick,target:b));
    }
    [Fact] public void InvitationsExpireAndLogoutInvalidatesHandoff()
    {
        var s=New();var a=Player(s);var b=Player(s);Command(s,a,SocialAction.Create);Command(s,a,SocialAction.Invite,target:b);var invite=Assert.Single(s.Invitations(b));now+=30000;s.Advance();Assert.Equal(SocialOutcome.Missing,Command(s,b,SocialAction.Accept,invite:invite));
        Command(s,a,SocialAction.Create,SocialKind.Guild,name:"Тестовая гильдия");Join(s,a,b,SocialKind.Guild);Command(s,a,SocialAction.Transfer,SocialKind.Guild,b);invite=Assert.Single(s.Invitations(b));s.SetOffline(a);s.SetOnline(a);Assert.Equal(SocialOutcome.Missing,Command(s,b,SocialAction.Accept,SocialKind.Guild,invite:invite));
    }
    [Fact] public void ReplayFingerprintAndRevisionNeverReapply()
    {
        var s=New();var a=Player(s);var command=new SocialCommand(1,SocialKind.Party,SocialAction.Create,0,0,0,0,false,"");Assert.Equal(SocialOutcome.Accepted,s.Execute(a,command));var id=s.Group(a,SocialKind.Party)!.Id;
        Assert.Equal(SocialOutcome.Accepted,s.Execute(a,command));Assert.Equal(id,s.Group(a,SocialKind.Party)!.Id);Assert.Equal(SocialOutcome.InvalidOperation,s.Execute(a,command with{Kind=SocialKind.Guild,Name="Test"}));
        Assert.Equal(SocialOutcome.Stale,s.Execute(a,new(2,SocialKind.Party,SocialAction.Resize,0,0,0,20,false,"")));Assert.Equal(6,s.Group(a,SocialKind.Party)!.Capacity);
        var restored=New();restored.Restore(s.Dirty.ToArray());restored.SetOnline(a);Assert.Equal(SocialOutcome.Stale,restored.Execute(a,new(2,SocialKind.Party,SocialAction.Resize,0,0,0,20,false,"")));
    }
    [Fact] public void GuildRolesAndExplicitTransferProtectLeader()
    {
        var s=New();var a=Player(s);var b=Player(s);var c=Player(s);var d=Player(s);Command(s,a,SocialAction.Create,SocialKind.Guild,name:"Guild");Join(s,a,b,SocialKind.Guild);Join(s,a,c,SocialKind.Guild);Join(s,a,d,SocialKind.Guild);
        Command(s,a,SocialAction.Promote,SocialKind.Guild,b);Command(s,a,SocialAction.Promote,SocialKind.Guild,c);
        Assert.Equal(SocialOutcome.NotAllowed,Command(s,b,SocialAction.Kick,SocialKind.Guild,c));Assert.Equal(SocialOutcome.NotAllowed,Command(s,b,SocialAction.Promote,SocialKind.Guild,d));Assert.Equal(SocialOutcome.Accepted,Command(s,b,SocialAction.Kick,SocialKind.Guild,d));
        Assert.Equal(SocialOutcome.NotAllowed,Command(s,a,SocialAction.Leave,SocialKind.Guild));Command(s,a,SocialAction.Transfer,SocialKind.Guild,b);Assert.Equal(a,s.Group(b,SocialKind.Guild)!.Leader);Command(s,b,SocialAction.Accept,SocialKind.Guild,invite:Assert.Single(s.Invitations(b)));Assert.Equal(b,s.Group(a,SocialKind.Guild)!.Leader);Assert.Equal(SocialOutcome.Accepted,Command(s,a,SocialAction.Leave,SocialKind.Guild));
        s.SetOffline(b);s.Advance();now+=200000;s.Advance();Assert.Equal(b,s.Group(c,SocialKind.Guild)!.Leader);
    }
    [Theory][InlineData("ab")][InlineData("bad\nname")][InlineData("bad\u200bname")][InlineData("bad/name")]
    public void InvalidNamesRejected(string name){var s=New();Assert.Equal(SocialOutcome.InvalidName,Command(s,Player(s),SocialAction.Create,SocialKind.Guild,name:name));}
    [Fact] public void GuildNamesNormalizeAndReleasedNameDoesNotReuseId()
    {
        var s=New();var a=Player(s);var b=Player(s);Command(s,a,SocialAction.Create,SocialKind.Guild,name:"  Test   Guild  ");var id=s.Group(a,SocialKind.Guild)!.Id;
        Assert.Equal(SocialOutcome.InvalidName,Command(s,b,SocialAction.Create,SocialKind.Guild,name:"test guild"));Command(s,a,SocialAction.Disband,SocialKind.Guild);Assert.Equal(SocialOutcome.Accepted,Command(s,b,SocialAction.Create,SocialKind.Guild,name:"TEST GUILD"));Assert.NotEqual(id,s.Group(b,SocialKind.Guild)!.Id);
    }
    [Fact] public void PartyAndGuildMembershipAreIndependentAndGuildNotFriendly()
    {var s=New();var a=Player(s);var b=Player(s);Command(s,a,SocialAction.Create,SocialKind.Guild,name:"Guild");Join(s,a,b,SocialKind.Guild);Assert.False(s.SameParty(a,b));Command(s,a,SocialAction.Create);Join(s,a,b);Assert.True(s.SameParty(a,b));Assert.Equal(SocialOutcome.InvalidState,Command(s,b,SocialAction.Create));Command(s,b,SocialAction.Leave);Assert.False(s.SameParty(a,b));Assert.NotNull(s.Group(b,SocialKind.Guild));}
    [Theory][InlineData("version")][InlineData("missingVersion")][InlineData("unknown")][InlineData("projection")][InlineData("leader")][InlineData("duplicate")]
    public void CorruptSocialRestoreFailsClosed(string problem)
    {
        var s=New();var a=Player(s);Command(s,a,SocialAction.Create);var rows=s.Dirty.ToArray();var index=Array.FindIndex(rows,r=>r.Kind==2);var row=rows[index];var json=System.Text.Json.Nodes.JsonNode.Parse(row.State)!.AsObject();
        switch(problem){case "version":json["Version"]=99;break;case "missingVersion":json.Remove("Version");break;case "unknown":json["Unknown"]=1;break;case "projection":rows[index]=row with{Members=[]};break;case "leader":json["Leader"]=Guid.NewGuid();break;case "duplicate":rows[index]=row with{State=row.State.Replace("\"Version\":1","\"Version\":1,\"Version\":1")};break;}
        if(problem is not ("projection" or "duplicate"))rows[index]=row with{State=json.ToJsonString()};Assert.ThrowsAny<Exception>(()=>New().Restore(rows));
    }
}
