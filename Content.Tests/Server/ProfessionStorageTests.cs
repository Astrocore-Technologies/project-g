using System.Text.Json.Nodes;
using Content.Server.Persistence;
using Content.Tests.Server.Persistence;
using Content.Shared.Network;
using Microsoft.Data.Sqlite;
using Xunit;
namespace Content.Tests.Server;
public sealed class ProfessionStorageTests
{
    private static SqliteConnection Connect(string path) { var c=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Pooling=false }.ToString()); c.Open(); return c; }
    [Fact] public async Task LegacyDocumentPreservesIdentityResourcesAndSkillMasteryThenConfirmedProfessionSurvivesRestart()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=ProfessionTests.World(); var initial=w.CreateInitialCharacter() with { Health=12,Mana=7 };
        var seed=await store.OpenAsync("",initial,CancellationToken.None); var token=seed.IssuedToken; var id=seed.CharacterId; await seed.DisposeAsync();
        var legacy=JsonNode.Parse(initial.Progression!.Serialize())!; legacy.AsObject().Remove("Profession");
        using (var c=Connect(store.DatabasePath)) using (var q=c.CreateCommand())
        { q.CommandText="UPDATE character_progression SET state=$state WHERE character_id=$id"; q.Parameters.AddWithValue("$id",id.ToString()); q.Parameters.AddWithValue("$state",legacy.ToJsonString()); q.ExecuteNonQuery(); }
        var session=await store.OpenAsync(token,initial,CancellationToken.None);
        Assert.Equal(id,session.CharacterId); Assert.Equal(12,session.State.Health); Assert.Equal(7,session.State.Mana); Assert.Equal(0,session.State.Progression!.Profession.SuccessfulUses);
        Assert.Equal(initial.Progression.Skills,session.State.Progression.Skills);
        var eligible=session.State with { Progression=session.State.Progression with { Discoveries=3,Profession=new() { SuccessfulUses=3 } } };
        var p=w.AddPlayer(42,new(1),eligible); var prepared=ProfessionTests.Command(w,p,1,ProfessionAction.Prepare); ProfessionTests.Command(w,p,2,ProfessionAction.Confirm,token:prepared.Confirmation);
        var saved=w.CaptureCharacter(42); await store.SaveAsync([new(session,saved)],CancellationToken.None); await session.DisposeAsync();
        store=new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None);
        await using var next=await store.OpenAsync(token,initial,CancellationToken.None); Assert.Equal(id,next.CharacterId); Assert.Equal(saved.Progression!.Serialize(),next.State.Progression!.Serialize());
        var restarted=ProfessionTests.World(); var owner=restarted.AddPlayer(42,new(1),next.State); Assert.Equal((ushort)1,restarted.ProfessionState(owner.EntityId,restarted.Tick).ActiveId);
        Assert.Equal(ProfessionOutcome.Unavailable,ProfessionTests.Command(restarted,owner,1,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
    }
    [Fact] public async Task RevisionConflictRollsBackSecretHistoryAndCoreTogether()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var initial=ProfessionTests.World().CreateInitialCharacter();
        await using var first=await store.OpenAsync("",initial,CancellationToken.None); await using var second=await store.OpenAsync("",initial,CancellationToken.None);
        await store.SaveAsync([new(second,second.State)],CancellationToken.None);
        var changed=first.State with { Health=7,Progression=first.State.Progression! with { Profession=new() { SuccessfulUses=1 } } };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([new(first,changed),new(second,second.State)],CancellationToken.None));
        using var c=Connect(store.DatabasePath); using var q=c.CreateCommand(); q.Parameters.AddWithValue("$id",first.CharacterId.ToString()); q.CommandText="SELECT state FROM character_progression WHERE character_id=$id";
        Assert.Equal(0,SavedProgression.Deserialize((string)q.ExecuteScalar()!).Profession.SuccessfulUses);
        q.CommandText="SELECT state FROM characters WHERE character_id=$id"; Assert.Equal(initial.Health,CharacterState.Deserialize((string)q.ExecuteScalar()!).Health);
    }
}
