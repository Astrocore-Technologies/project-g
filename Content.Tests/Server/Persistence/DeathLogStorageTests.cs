using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Shared.Combat;
using Microsoft.Data.Sqlite;
using Xunit;
namespace Content.Tests.Server.Persistence;

public sealed class DeathLogStorageTests
{
    [Fact]public async Task OrdinaryCheckpointsDoNotRewriteHistoryAndFailedNewDeathDoesNotAdvanceTheCache()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var world=CharacterPersistenceTests.World();
        var session=await store.OpenAsync("",world.CreateInitialCharacter(),default);var token=session.IssuedToken;
        var player=world.AddPlayer(42,new(1),session.State);var combat=world.Combat!;var id=player.EntityId;
        combat.PayHealth(id,combat.Get(id).Health,true);var first=world.CaptureCharacter(42);
        await store.SaveAsync([new(session,first)],default);session.Revision++;
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=store.DatabasePath,Pooling=false}.ToString());connection.Open();
        using var query=connection.CreateCommand();query.CommandText="CREATE TRIGGER reject_journal_rewrite BEFORE DELETE ON character_death_logs BEGIN SELECT RAISE(ABORT,'history rewrite'); END";query.ExecuteNonQuery();
        await store.SaveAsync([new(session,first with {X=first.X+.1f})],default);session.Revision++;
        Assert.True(combat.Resurrect(default,id,1,10));combat.PayHealth(id,10,true);var second=world.CaptureCharacter(42);
        await Assert.ThrowsAsync<SqliteException>(()=>store.SaveAsync([new(session,second)],default));
        query.CommandText="DROP TRIGGER reject_journal_rewrite";query.ExecuteNonQuery();
        await store.SaveAsync([new(session,second)],default);await session.DisposeAsync();
        await using var restored=await store.OpenAsync(token,world.CreateInitialCharacter(),default);
        Assert.Equal(2UL,restored.State.DeathLogs![^1].DeathId);Assert.Equal(2UL,restored.State.Life!.DeathId);
    }
    [Fact]public async Task TenDeathReportsSurviveSqliteReopenWhileTheCurrentSessionStartsEmpty()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var world=CharacterPersistenceTests.World();
        var session=await store.OpenAsync("",world.CreateInitialCharacter(),default);var token=session.IssuedToken;
        var player=world.AddPlayer(42,new(1),session.State);var combat=world.Combat!;
        for(var i=0;i<12;i++)
        {
            combat.PayHealth(player.EntityId,combat.Get(player.EntityId).Health,true);
            if(i<11)Assert.True(combat.Resurrect(default,player.EntityId,(ulong)i+1,110));
        }
        var state=world.CaptureCharacter(42);Assert.Equal(10,state.DeathLogs!.Count);Assert.NotEmpty(state.SessionJournal!);
        await store.SaveAsync([new(session,state)],default);await session.DisposeAsync();
        await using var restored=await store.OpenAsync(token,world.CreateInitialCharacter(),default);
        Assert.Equal(10,restored.State.DeathLogs!.Count);Assert.Equal(3UL,restored.State.DeathLogs[0].DeathId);Assert.Null(restored.State.SessionJournal);
        var next=CharacterPersistenceTests.World();var actor=next.AddPlayer(42,new(1),restored.State);
        Assert.Equal(10,next.Combat!.DeathLogs(actor.EntityId).Count);Assert.Empty(next.Combat.CurrentJournal(actor.EntityId));
        Assert.Equal(12UL,next.Combat.CaptureLife(actor.EntityId).DeathId);Assert.Equal(0,next.Combat.Get(actor.EntityId).Health);
    }
    [Fact]public async Task AStaleOtherCharacterRollsBackDeathHpAndJournalTogether()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var world=CharacterPersistenceTests.World();var initial=world.CreateInitialCharacter();
        var first=await store.OpenAsync("",initial,default);var second=await store.OpenAsync("",initial,default);var token=first.IssuedToken;
        var player=world.AddPlayer(42,new(1),initial);world.Combat!.PayHealth(player.EntityId,initial.Health,true);var dead=world.CaptureCharacter(42);
        await store.SaveAsync([new(second,initial with {Health=50})],default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([new(first,dead),new(second,initial with {Health=40})],default));
        await first.DisposeAsync();await second.DisposeAsync();
        await using var restored=await store.OpenAsync(token,initial,default);
        Assert.Equal(initial.Health,restored.State.Health);Assert.Empty(restored.State.DeathLogs!);Assert.Null(restored.State.Life);
    }
    [Fact]public void FrozenRegionTransferDeepCopiesBothCurrentAndSavedEvidence()
    {
        var world=CharacterPersistenceTests.World();var player=world.AddPlayer(42,new(1));var combat=world.Combat!;
        combat.PayHealth(player.EntityId,combat.Get(player.EntityId).Health,true);var state=world.CaptureCharacter(42);var frozen=FrozenCharacterState.Capture(state);
        state.DeathLogs![0].Entries[0]=default;((CombatJournalEntry[])state.SessionJournal!)[0]=default;
        var restored=frozen.Restore();Assert.True(restored.DeathLogs![0].Entries[0].IsValid);Assert.True(restored.SessionJournal![0].IsValid);
        var next=CharacterPersistenceTests.World();var actor=next.AddPlayer(42,new(1),restored,restoringFromLogout:false);
        Assert.NotEmpty(next.Combat!.CurrentJournal(actor.EntityId));Assert.Single(next.Combat.DeathLogs(actor.EntityId));
    }
    [Fact]public async Task AdditiveMigrationFromNinePreservesCharacterAndIsIdempotent()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var initial=CharacterPersistenceTests.World().CreateInitialCharacter() with {Health=17};
        var session=await store.OpenAsync("",initial,default);var token=session.IssuedToken;await session.DisposeAsync();
        using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=store.DatabasePath,Pooling=false}.ToString()))
        {connection.Open();using var query=connection.CreateCommand();query.CommandText="DROP TABLE character_death_logs; DELETE FROM schema_version WHERE version=10";query.ExecuteNonQuery();}
        await store.InitializeAsync(default);await store.InitializeAsync(default);
        await using var restored=await store.OpenAsync(token,initial,default);Assert.Equal(17,restored.State.Health);Assert.Empty(restored.State.DeathLogs!);
    }
}
