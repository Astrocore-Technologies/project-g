using System.Diagnostics;
using System.Numerics;
using Content.Server.Networking;
using Content.Tests.Server.Networking;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class SqliteServerProcessTests
{
    [Fact]
    public async Task ConfirmedMovementAndManaSurviveProcessCrashAndSqliteReopen()
    {
        var probe = new SqliteCharacterStore();
        var port = CharacterPersistenceTests.FreePort();
        string token; Guid character; double mana;
        var target = new Vector2(-6, 2);
        using (var child = StartServer(port, probe.DatabasePath))
        {
            try
            {
                using var client = new NetworkMovementIntegrationTests.TestClient(port);
                await CharacterPersistenceTests.Poll(client, () => client.Spawns.Count != 0 && client.Loadouts.Count != 0);
                token = client.Token; character = probe.SingleId;
                var id = client.LocalSpawn.EntityId;
                var before = client.Loadouts[id].Mana;
                client.Ability(1, 1, Vector2.Normalize(new Vector2(2, 3)));
                await CharacterPersistenceTests.Poll(client, () => client.Loadouts[id].Mana < before);
                mana = client.Loadouts[id].Mana;
                uint sequence = 0;
                await CharacterPersistenceTests.Poll(client, () => { client.Move(++sequence, target); return client.IsAt(id, target); });
                // Kill only this test's newly created child. No graceful-save path is allowed here.
                child.Process.Kill(); await child.Process.WaitForExitAsync();
            }
            finally { await StopOwnChild(child.Process); }
        }
        using (var child = StartServer(port, probe.DatabasePath))
        {
            try
            {
                using var restored = new NetworkMovementIntegrationTests.TestClient(port, token);
                await CharacterPersistenceTests.Poll(restored, () => restored.Spawns.Count != 0 && restored.Loadouts.Count != 0);
                Assert.Equal(target, restored.LocalSpawn.Position);
                Assert.Equal(mana, restored.Loadouts[restored.LocalSpawn.EntityId].Mana);
                Assert.Equal(character, probe.SingleId);
                Assert.Equal(1, probe.Count);
            }
            finally { await StopOwnChild(child.Process); }
        }
    }

    private static Child StartServer(int port, string database)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        // Use test deps/runtime config to execute the actual copied server entry point in any build layout.
        start.ArgumentList.Add("exec"); start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Content.Tests.runtimeconfig.json"));
        start.ArgumentList.Add("--depsfile"); start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Content.Tests.deps.json"));
        start.ArgumentList.Add(typeof(GameServerService).Assembly.Location);
        start.ArgumentList.Add($"--Server:Port={port}");
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["Persistence__Provider"] = "Sqlite";
        start.Environment["Persistence__SqlitePath"] = database;
        var process = Process.Start(start) ?? throw new InvalidOperationException("Test server process could not start.");
        return new(process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
    }

    private static async Task StopOwnChild(Process process)
    {
        if (!process.HasExited) process.Kill();
        await process.WaitForExitAsync();
    }

    private sealed record Child(Process Process, Task<string> Output, Task<string> Error) : IDisposable
    {
        public void Dispose() => Process.Dispose();
    }
}
