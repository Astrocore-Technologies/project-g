using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Networking;

public sealed class WelcomeRejoinTests
{
    [Fact]
    public async Task NewIdentityReceivesCredentialOnlyOnceWhenRetainedSessionRejoins()
    {
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(default);
        var world = CharacterPersistenceTests.World();
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance, store);
        await server.StartAsync(default);
        using var first = new NetworkMovementIntegrationTests.TestClient(port, lossy: false);
        try
        {
            await Poll(first, () => first.Welcome.HasValue && first.Spawns.Count > 0);
            var token = first.Token;
            Assert.True(NetworkProtocol.IsDevelopmentToken(token) && token.Length > 0);
            var entity = first.LocalSpawn.EntityId;
            first.Disconnect();
            await Poll(first, () => server.DetachedCombatantCount == 1);
            using var rejoined = new NetworkMovementIntegrationTests.TestClient(port, token, lossy: false);
            await Poll(rejoined, () => rejoined.Welcome.HasValue && rejoined.Spawns.Count > 0);
            Assert.Equal(entity, rejoined.LocalSpawn.EntityId);
            Assert.Equal(0, server.DetachedCombatantCount);
            Assert.True(rejoined.Welcome!.Value.DevelopmentToken.Length == 0);
            Assert.True(rejoined.Token == token);
            Assert.Null(rejoined.Rejection);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await server.StopAsync(timeout.Token);
        }
    }

    private static async Task Poll(NetworkMovementIntegrationTests.TestClient client, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            client.Poll();
            if (condition()) return;
            await Task.Delay(10, timeout.Token);
        }
    }
}
