using Content.Database;
using Content.Server.Persistence;
using Xunit;
namespace Content.Tests.Server;
// Explicit test-only database: absent configuration is a visible skip, never a successful no-op.
public sealed class LivePostgresFactAttribute:FactAttribute
{
    public LivePostgresFactAttribute(){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECT_G_TEST_POSTGRES")))Skip="Set PROJECT_G_TEST_POSTGRES to an isolated disposable test database.";}
}
public sealed class LivePostgresPvpTests
{
    private static async Task Run(Func<Func<ICharacterStore>,Task> scenario)
    {
        var sources=new List<PostgresCharacterStore>();
        ICharacterStore Create(){var db=new PostgresCharacterStore(Environment.GetEnvironmentVariable("PROJECT_G_TEST_POSTGRES")!);sources.Add(db);return new DatabaseCharacterStore(db);}
        try{await scenario(Create);}finally{foreach(var db in sources)await db.DisposeAsync();}
    }
    [LivePostgresFact]public Task DeathDropRestartPickupHasOneDurableFullInstance()=>Run(PvpPersistenceTests.DeathRoundTrip);
    [LivePostgresFact]public Task StaleWorldFenceRollsBackDeathAndExpiryIsTerminal()=>Run(PvpPersistenceTests.ExpiryAndFence);
}
