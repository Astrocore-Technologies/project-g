using Content.Server.Persistence;
using Content.Database;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;
namespace Content.Tests.Server;
public sealed class WorldNodeStorageTests
{
    internal static SqliteConnection Connect(string path) { var c=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Pooling=false }.ToString()); c.Open(); return c; }
    [Fact] public async Task CommonCommitKeepsWorldHistoryCharacterAndAuditThenRestoresAllAfterRestart()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=WorldNodeTests.World(1); var initial=WorldNodeTests.Initial(w,true);
        var owner=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None); w.RestoreWorldNode(owner.State,owner.Revision);
        var session=await store.OpenAsync("",initial,CancellationToken.None); var credential=session.IssuedToken; var id=session.CharacterId; var p=w.AddPlayer(42,new(1),session.State); w.BindWorldActor(42,id);
        WorldNodeTests.Command(w,p,1,WorldNodeAction.Repair); WorldNodeTests.Command(w,p,2,WorldNodeAction.Patrol);
        await store.SaveWithWorldAsync([new(session,w.CaptureCharacter(42))],new(owner,w.CaptureWorldNode(),w.WorldNodeAudit),CancellationToken.None);
        using(var c=Connect(store.DatabasePath)) using(var q=c.CreateCommand()) { q.CommandText="SELECT count(*) FROM world_audit"; Assert.Equal(2L,q.ExecuteScalar()); q.CommandText="SELECT actor FROM world_audit LIMIT 1"; Assert.Equal(id.ToString("N"),q.ExecuteScalar()); }
        await owner.DisposeAsync(); await session.DisposeAsync(); store=new(store.DatabasePath); await store.InitializeAsync(CancellationToken.None);
        await using var restartedOwner=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None); var restart=WorldNodeTests.World(1); restart.RestoreWorldNode(restartedOwner.State,restartedOwner.Revision);
        await using var next=await store.OpenAsync(credential,initial,CancellationToken.None); Assert.Equal(id,next.CharacterId); Assert.Equal((byte)3,next.State.Progression!.WorldParticipation.Contributions); Assert.Equal((byte)3,restart.PublicWorldNode().Consequences); Assert.Equal(initial.Health,next.State.Health); Assert.Equal(initial.Mana,next.State.Mana);
    }
    [Fact] public async Task StaleCharacterOrWorldRevisionRollsBackBothStateAndAudit()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=WorldNodeTests.World(1); var initial=WorldNodeTests.Initial(w);
        await using var owner=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None); await using var session=await store.OpenAsync("",initial,CancellationToken.None);
        var audit=new DatabaseWorldAudit("test","Repair","test",1); var changed=initial with { Health=7,Progression=initial.Progression! with { WorldParticipation=new() { Contributions=1 } } };
        await store.SaveAsync([new(session,initial)],CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveWithWorldAsync([new(session,changed)],new(owner,new() { Repairs=1 },[audit]),CancellationToken.None));
        await store.SaveWithWorldAsync([],new(owner,new() { StormRumor=true },[audit]),CancellationToken.None);
        await using var fresh=await store.OpenAsync("",initial,CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveWithWorldAsync([new(fresh,changed)],new(owner,new() { Repairs=1 },[audit]),CancellationToken.None));
        using(var connection=Connect(store.DatabasePath)) using(var query=connection.CreateCommand()) { query.CommandText="SELECT state FROM characters WHERE character_id=$id"; query.Parameters.AddWithValue("$id",fresh.CharacterId.ToString()); Assert.Equal(initial.Health,CharacterState.Deserialize((string)query.ExecuteScalar()!).Health); }
        using var c=Connect(store.DatabasePath); using var q=c.CreateCommand(); q.CommandText="SELECT state FROM world_nodes"; var state=SavedWorldNode.Deserialize((string)q.ExecuteScalar()!); Assert.Equal(0,state.Repairs); Assert.True(state.StormRumor);
        q.CommandText="SELECT count(*) FROM world_audit"; Assert.Equal(1L,q.ExecuteScalar()); q.CommandText="SELECT state FROM character_progression"; Assert.Equal((byte)0,SavedProgression.Deserialize((string)q.ExecuteScalar()!).WorldParticipation.Contributions);
    }
    [Fact] public async Task WorldOwnerIsExclusiveAndReleaseAllowsAnotherProcessOwner()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var other=new SqliteCharacterStore(store.DatabasePath);
        var owner=await store.OpenWorldAsync("prototype-crossing",new(),CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(()=>other.OpenWorldAsync("prototype-crossing",new(),CancellationToken.None)); await owner.DisposeAsync(); await using var next=await other.OpenWorldAsync("prototype-crossing",new(),CancellationToken.None); Assert.True(next.Revision>owner.Revision);
    }
    [Fact] public async Task AdditiveSchemaFiveMigrationPreservesExistingCharacterAndProgression()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var initial=WorldNodeTests.Initial(WorldNodeTests.World()) with { Health=12,Mana=7 };
        var seed=await store.OpenAsync("",initial,CancellationToken.None); var token=seed.IssuedToken; var id=seed.CharacterId; await seed.DisposeAsync();
        using(var c=Connect(store.DatabasePath)) using(var q=c.CreateCommand()) { q.CommandText="DROP TABLE world_audit; DROP TABLE world_nodes; DELETE FROM schema_version WHERE version=6"; q.ExecuteNonQuery(); }
        await store.InitializeAsync(CancellationToken.None); await using var restored=await store.OpenAsync(token,initial,CancellationToken.None); Assert.Equal(id,restored.CharacterId); Assert.Equal(12,restored.State.Health); Assert.Equal(7,restored.State.Mana); Assert.Equal(initial.Progression!.Serialize(),restored.State.Progression!.Serialize());
        await using var owner=await store.OpenWorldAsync("prototype-crossing",new(),CancellationToken.None); Assert.Equal(0,owner.State.Repairs);
    }
}
