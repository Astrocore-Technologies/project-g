using Content.Server.Persistence;
using Content.Tests.Server.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;
namespace Content.Tests.Server;
public sealed class StarterZoneStorageTests
{
    [Fact] public async Task ExploredMapAndTutorialSurviveSqliteRestartWithoutDuplicatingEchoOrRewards()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=StarterZoneTests.World(true); var initial=w.CreateInitialCharacter();
        var session=await store.OpenAsync("",initial,CancellationToken.None); var token=session.IssuedToken; var id=session.CharacterId;
        var p=w.AddPlayer(42,new(1),session.State); StarterZoneTests.Walk(w,42,new(-9,-6)); var state=w.CaptureCharacter(42);
        await store.SaveAsync([new(session,state)],CancellationToken.None); await session.DisposeAsync();
        store=new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None);
        await using var restored=await store.OpenAsync(token,initial,CancellationToken.None); Assert.Equal(id,restored.CharacterId);
        Assert.Equal(state.Progression!.Serialize(),restored.State.Progression!.Serialize()); Assert.Equal(state.Echoes!.Serialize(),restored.State.Echoes!.Serialize());
        var restart=StarterZoneTests.World(true); var next=restart.AddPlayer(42,new(1),restored.State); restart.Simulate(0.05f);
        Assert.Equal(w.ExplorationState(p.EntityId).Cells,restart.ExplorationState(next.EntityId).Cells); Assert.Equal(state.Progression.Experience,restart.CaptureCharacter(42).Progression!.Experience);
    }
    [Fact] public async Task ExplorationRollsBackWithCharacterWhenAnyRevisionFenceFails()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=StarterZoneTests.World(); var initial=w.CreateInitialCharacter();
        await using var a=await store.OpenAsync("",initial,CancellationToken.None); await using var b=await store.OpenAsync("",initial,CancellationToken.None);
        await store.SaveAsync([new(b,b.State)],CancellationToken.None);
        w.AddPlayer(42,new(1),a.State); StarterZoneTests.Walk(w,42,new(-9,-6));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([new(a,w.CaptureCharacter(42)),new(b,b.State)],CancellationToken.None));
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=store.DatabasePath,Pooling=false }.ToString()); c.Open(); using var q=c.CreateCommand();
        q.CommandText="SELECT state FROM character_progression WHERE character_id=$id"; q.Parameters.AddWithValue("$id",a.CharacterId.ToString());
        Assert.Null(SavedProgression.Deserialize((string)q.ExecuteScalar()!).Exploration);
        q.CommandText="SELECT state FROM characters WHERE character_id=$id"; Assert.Equal(initial.Z,CharacterState.Deserialize((string)q.ExecuteScalar()!).Z);
    }
}
