using System.Text.Json.Nodes;
using Content.Server.Persistence;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class CombatModelMigrationTests
{
    private static ServerWorld World() => new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        catalog:ContentCatalogTests.Load(),inventory:Options.Create(new InventoryOptions { Enabled=true }),hitRoll:()=>0,criticalRoll:()=>1);
    [Fact]
    public async Task LegacyCharacterResetsOnceUnderSameCredentialAndNewStatsRemainDurable()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var initial=World().CreateInitialCharacter();
        var session=await store.OpenAsync("",initial,CancellationToken.None);
        var credential=session.IssuedToken; var id=session.CharacterId; await session.DisposeAsync();
        var legacy=JsonNode.Parse(initial.Serialize())!; legacy["Version"]=1; legacy["Stats"]!.AsObject().Remove("Wisdom");
        legacy["Health"]=0;
        using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=store.DatabasePath,Pooling=false }.ToString()))
        {
            connection.Open(); using var query=connection.CreateCommand();
            query.CommandText="UPDATE characters SET state=$state WHERE character_id=$id";
            query.Parameters.AddWithValue("$state",legacy.ToJsonString()); query.Parameters.AddWithValue("$id",id.ToString()); query.ExecuteNonQuery();
        }
        var replacement=World().CreateInitialCharacter();
        session=await store.OpenAsync(credential,replacement,CancellationToken.None);
        Assert.Equal(id,session.CharacterId); Assert.Equal(2,session.State.Version); Assert.Equal(0,session.State.Stats.Wisdom);
        Assert.Equal(20,session.State.Progression!.StatPoints); Assert.Equal(replacement.Health,session.State.Health);
        Assert.Equal(replacement.Inventory!.Serialize(),session.State.Inventory!.Serialize());
        var changed=session.State with { Health=7,Progression=session.State.Progression with { StatPoints=3,Allocation=null } };
        await store.SaveAsync([new(session,changed)],CancellationToken.None); await session.DisposeAsync();
        await using var restored=await store.OpenAsync(credential,replacement,CancellationToken.None);
        Assert.Equal(7,restored.State.Health); Assert.Equal(3,restored.State.Progression!.StatPoints); Assert.Equal(1,store.Count);
    }

    [Fact]
    public void UnknownOrCorruptVersionIsNotClassifiedAsAnApprovedLegacyReset()
    {
        var state=CharacterPersistenceTests.World().CreateInitialCharacter();
        Assert.False(CombatModelMigration.RequiresReset(state.Serialize()));
        var json=JsonNode.Parse(state.Serialize())!; json["Version"]=99;
        Assert.False(CombatModelMigration.RequiresReset(json.ToJsonString()));
        json["Version"]=1; json["Health"]=-1;
        Assert.Throws<InvalidDataException>(()=>CombatModelMigration.RequiresReset(json.ToJsonString()));
    }
}
