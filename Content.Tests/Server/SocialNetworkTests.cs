using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Content.Shared.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class SocialNetworkTests
{
    [Fact]public async Task LossyPrivateInvitesPublishOnlyAfterCommitAndReconnectKeepsMembership()
    {
        var store=new SqliteCharacterStore();var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),PvpTests.World(),NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var a=new NetworkMovementIntegrationTests.TestClient(port);using var b=new NetworkMovementIntegrationTests.TestClient(port);using var outsider=new NetworkMovementIntegrationTests.TestClient(port);
        async Task Poll(Func<bool> done){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){timeout.Token.ThrowIfCancellationRequested();a.Poll();b.Poll();outsider.Poll();if(done())return;await Task.Delay(10);}}
        TaskCompletionSource? gate=null;
        try
        {
            await Poll(()=>a.SocialRosters.Count==2&&b.SocialRosters.Count==2&&outsider.SocialRosters.Count==2);
            gate=store.PauseSaves();a.Social(new(1,SocialKind.Party,SocialAction.Create,0,0,0,0,false,""));await Poll(()=>store.PendingSave);
            for(var i=0;i<30;i++){a.Poll();b.Poll();outsider.Poll();await Task.Delay(10);}Assert.Equal(0UL,a.SocialRosters[SocialKind.Party].Id);Assert.Empty(a.SocialResults);
            gate.SetResult();await Poll(()=>a.SocialRosters[SocialKind.Party].Id!=0);var group=a.SocialRosters[SocialKind.Party];
            a.Social(new(2,SocialKind.Party,SocialAction.Invite,b.LocalSpawn.EntityId.Value,group.Revision,0,0,false,""));await Poll(()=>b.SocialInvitations.Any(i=>i.Items.Length==1));var invite=b.SocialInvitations.Last().Items.Single();
            Assert.All(outsider.SocialInvitations,i=>Assert.Empty(i.Items));Assert.Equal(0UL,outsider.SocialRosters[SocialKind.Party].Id);
            b.Social(new(1,SocialKind.Party,SocialAction.Accept,0,invite.Revision,invite.Token,0,true,""));await Poll(()=>b.SocialRosters[SocialKind.Party].Total==2&&a.SocialRosters[SocialKind.Party].Total==2&&b.PartyPresences.Any(p=>p.Members.Length==2));
            var self=b.SocialRosters[SocialKind.Party].Self;var token=b.Token;var old=b.LocalSpawn.EntityId;b.Disconnect();await Poll(()=>!a.Spawns.ContainsKey(old));
            using var reconnected=new NetworkMovementIntegrationTests.TestClient(port,token);await CharacterPersistenceTests.Poll(reconnected,()=>reconnected.SocialRosters.Count==2);Assert.Equal(group.Id,reconnected.SocialRosters[SocialKind.Party].Id);Assert.Equal(self,reconnected.SocialRosters[SocialKind.Party].Self);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }
}
