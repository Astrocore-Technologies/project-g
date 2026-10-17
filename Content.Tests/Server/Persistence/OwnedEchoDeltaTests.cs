using Content.Database;
using Content.Server.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class OwnedEchoDeltaTests
{
    private static SavedEchoCombat Echo()=>new()
    {
        InstanceId=Guid.NewGuid(),DefinitionId="test_guardian_echo",ProfileId="mira_combat",Availability=SavedEchoAvailability.Ready,
        Stats=new(10,10,10,10,10,10,10),X=1,Z=1,Health=50,Mana=30,Defense=new(1,40,0,0),CombatControl=new(1,0,0,0,0,0),
        AttackCooldownSeconds=0,Cooldowns=[new("mira_signature",0)],SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };
    private static void Sql(string path,string sql)
    {using var c=WorldNodeStorageTests.Connect(path);using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
    private static long Count(string path,string operation)
    {using var c=WorldNodeStorageTests.Connect(path);using var q=c.CreateCommand();q.CommandText="SELECT count(*) FROM echo_writes WHERE operation=$op";q.Parameters.AddWithValue("$op",operation);return(long)q.ExecuteScalar()!;}
    private static void Audit(string path)=>Sql(path,"""
        CREATE TABLE echo_writes(operation TEXT NOT NULL);
        CREATE TRIGGER echo_insert AFTER INSERT ON character_owned_echoes BEGIN INSERT INTO echo_writes VALUES('insert'); END;
        CREATE TRIGGER echo_update AFTER UPDATE ON character_owned_echoes BEGIN INSERT INTO echo_writes VALUES('update'); END;
        CREATE TRIGGER echo_delete AFTER DELETE ON character_owned_echoes BEGIN INSERT INTO echo_writes VALUES('delete'); END;
        """);

    [Fact]public async Task DomainCheckpointWritesOnlyChangedAddedAndRemovedRowsAmongFiftyOwnedEchoes()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var initial=CharacterPersistenceTests.World().CreateInitialCharacter();
        var rows=Enumerable.Range(0,50).Select(_=>Echo()).ToArray();var state=initial with {OwnedEchoes=rows};
        var session=await store.OpenAsync("",initial,default);var token=session.IssuedToken;
        await store.SaveAsync([new(session,state)],default);session.Revision++;Audit(store.DatabasePath);
        await store.SaveAsync([new(session,state with {Mana=20})],default);session.Revision++;
        Assert.Equal(0,Count(store.DatabasePath,"insert"));Assert.Equal(0,Count(store.DatabasePath,"update"));Assert.Equal(0,Count(store.DatabasePath,"delete"));
        rows[0]=rows[0] with {Mana=12};await store.SaveAsync([new(session,state)],default);session.Revision++;
        Assert.Equal(1,Count(store.DatabasePath,"update"));Assert.Equal(0,Count(store.DatabasePath,"insert"));Assert.Equal(0,Count(store.DatabasePath,"delete"));
        var added=Echo();state=state with {OwnedEchoes=[rows[0],..rows.Skip(2),added]};
        await store.SaveAsync([new(session,state)],default);await session.DisposeAsync();
        Assert.Equal(1,Count(store.DatabasePath,"update"));Assert.Equal(1,Count(store.DatabasePath,"insert"));Assert.Equal(1,Count(store.DatabasePath,"delete"));
        await using var loaded=await store.OpenAsync(token,initial,default);Assert.Equal(50,loaded.State.OwnedEchoes!.Count);
        Assert.Equal(12,loaded.State.OwnedEchoes.Single(e=>e.InstanceId==rows[0].InstanceId).Mana);
        Assert.DoesNotContain(loaded.State.OwnedEchoes,e=>e.InstanceId==rows[1].InstanceId);
    }
    [Fact]public async Task StaleBatchRollsBackDeltaAndItsCacheSoRetryStillWritesTheChangedValue()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var initial=CharacterPersistenceTests.World().CreateInitialCharacter();
        var echo=Echo();var state=initial with {OwnedEchoes=new[]{echo}};
        var first=await store.OpenAsync("",initial,default);var second=await store.OpenAsync("",initial,default);var token=first.IssuedToken;
        await store.SaveAsync([new(first,state)],default);first.Revision++;Audit(store.DatabasePath);
        await store.SaveAsync([new(second,initial)],default); // Deliberately leave this session's expected revision stale.
        state=state with {Mana=11,OwnedEchoes=[echo with {Mana=7}]};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([new(first,state),new(second,initial)],default));
        Assert.Equal(0,Count(store.DatabasePath,"update"));
        second.Revision++;
        await store.SaveAsync([new(first,state),new(second,initial)],default);
        Assert.Equal(1,Count(store.DatabasePath,"update"));await first.DisposeAsync();await second.DisposeAsync();
        await using var loaded=await store.OpenAsync(token,initial,default);Assert.Equal(11,loaded.State.Mana);Assert.Equal(7,Assert.Single(loaded.State.OwnedEchoes!).Mana);
    }
    [Fact]public async Task DeltaCannotTakeForeignEchoAndFailureRestoresDeletedRowsAndCharacterResources()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"sqlite-tests",Guid.NewGuid().ToString("N"),"echo-delta.db");
        var store=new DevelopmentSqliteCharacterStore(path);await store.InitializeAsync(default);
        var first=await store.OpenAsync("","{}","{}","{}",default);var second=await store.OpenAsync("","{\"hp\":17}","{}","{}",default);
        var token=second.IssuedToken;var foreign=new DatabaseEchoRow(Guid.NewGuid(),1,"{}");var own=new DatabaseEchoRow(Guid.NewGuid(),1,"{}");
        await store.SaveAsync([new(first,first.Revision,"{}",null,OwnedEchoes:[foreign]),new(second,second.Revision,second.State,null,OwnedEchoes:[own])],default);
        var revision=second.Revision+1;
        await Assert.ThrowsAsync<SqliteException>(()=>store.SaveAsync([new(second,revision,"{\"hp\":0}",null,EchoChanges:new([foreign],[own.InstanceId]))],default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([new(second,revision,"{\"hp\":0}",null,EchoChanges:new([],[foreign.InstanceId]))],default));
        foreach(var changes in new[]{new DatabaseEchoChanges([own],[own.InstanceId]),new([], [own.InstanceId,own.InstanceId]),new([own with {Version=2}],[]),new([own with {State=new string('я',4097)}],[])})
            await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAsync([new(second,revision,"{\"hp\":0}",null,EchoChanges:changes)],default));
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAsync([new(second,revision,"{}",null,OwnedEchoes:[],EchoChanges:new([],[]))],default));
        await first.DisposeAsync();await second.DisposeAsync();await using var restored=await store.OpenAsync(token,"{}","{}","{}",default);
        Assert.Equal("{\"hp\":17}",restored.State);Assert.Equal(own,Assert.Single(restored.OwnedEchoes));
    }
}
