using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class ProfessionIntegrationTests
{
    [Fact] public async Task LossyClientsSeeOnlyRevealedOwnProfessionAndConfirmWaitsForCommitThenReconnects()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world=ProfessionTests.World(); var initial=ProfessionTests.Eligible(world);
        var seed=await store.OpenAsync("",initial,CancellationToken.None); var token=seed.IssuedToken; await seed.DisposeAsync(); var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions { Port=port,NetworkPollIntervalMilliseconds=1 }),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(CancellationToken.None);
        using var first=new NetworkMovementIntegrationTests.TestClient(port,token); using var second=new NetworkMovementIntegrationTests.TestClient(port);
        async Task Poll(Func<bool> condition)
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true) { deadline.Token.ThrowIfCancellationRequested(); first.Poll(); second.Poll(); if (condition()) return; await Task.Delay(25,deadline.Token); }
        }
        try
        {
            await Poll(()=>first.Professions.Count==1 && second.Professions.Count==1 && first.Progressions.Count==1);
            var id=first.LocalSpawn.EntityId; Assert.Equal((ushort)1,first.Professions[id].OfferedId); Assert.Equal("",second.Professions[second.LocalSpawn.EntityId].OfferedName);
            Assert.DoesNotContain(first.Progressions[id].Skills,s=>s.Id==6);
            first.Profession(new(1,ProfessionAction.Prepare,1,0)); await Poll(()=>first.ProfessionResults.Count==1);
            var prepared=first.ProfessionResults[0]; Assert.Equal(ProfessionOutcome.Prepared,prepared.Outcome); Assert.Equal((ushort)0,first.Professions[id].ActiveId);
            var gate=store.PauseSaves();
            try
            {
                first.Profession(new(2,ProfessionAction.Confirm,1,prepared.Confirmation)); await Poll(()=>store.PendingSave);
                for (var i=0;i<20;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Single(first.ProfessionResults); Assert.Equal((ushort)0,first.Professions[id].ActiveId); Assert.DoesNotContain(first.Progressions[id].Skills,s=>s.Id==6);
                gate.TrySetResult(); await Poll(()=>first.ProfessionResults.Count==2 && first.Professions[id].ActiveId==1 && first.Progressions[id].Skills.Any(s=>s.Id==6));
                Assert.Equal(ProfessionOutcome.Accepted,first.ProfessionResults[1].Outcome); Assert.Single(first.Professions); Assert.Single(second.Professions); Assert.Empty(second.ProfessionResults);
                first.Profession(new(2,ProfessionAction.Confirm,1,prepared.Confirmation));
                for (var i=0;i<20;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Equal(2,first.ProfessionResults.Count); Assert.Single(first.Progressions[id].Skills,s=>s.Id==6);
                first.Disconnect(); await Task.Delay(500); using var reconnect=new NetworkMovementIntegrationTests.TestClient(port,token);
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                while (reconnect.Professions.Count==0 || reconnect.Progressions.Count==0) { reconnect.Poll(); second.Poll(); deadline.Token.ThrowIfCancellationRequested(); await Task.Delay(25,deadline.Token); }
                Assert.Equal((ushort)1,reconnect.Professions[reconnect.LocalSpawn.EntityId].ActiveId); Assert.Single(reconnect.Progressions[reconnect.LocalSpawn.EntityId].Skills,s=>s.Id==6);
            }
            finally { gate.TrySetResult(); }
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }
}
