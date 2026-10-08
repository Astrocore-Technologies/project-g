using Content.Database;
using Content.Server.Persistence;
using Content.Server.Social;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;
namespace Content.Tests.Server;
public sealed class SocialPersistenceTests
{
    [Fact]public Task SqliteMembershipAndReceiptsSurviveRestart(){var path=new SqliteCharacterStore().DatabasePath;return RoundTrip(()=>new SqliteCharacterStore(path));}
    [Fact]public Task SqliteStaleRealmFenceRollsBackCharacterAndMembership(){var path=new SqliteCharacterStore().DatabasePath;return Fence(()=>new SqliteCharacterStore(path));}
    internal static async Task RoundTrip(Func<ICharacterStore> create)
    {
        var store=create();await store.InitializeAsync(default);var initial=PvpTests.Initial(PvpTests.World());
        var a=await store.OpenAsync("",initial,default);var b=await store.OpenAsync("",initial with{Inventory=SavedInventory.Empty},default);var token=b.IssuedToken;
        var social=(ISocialStore)store;var lease=await social.OpenSocialAsync(default);var state=new SocialSimulation(()=>100000);state.Restore(lease.Rows);state.SetOnline(a.CharacterId);state.SetOnline(b.CharacterId);
        SocialTests.Command(state,a.CharacterId,SocialAction.Create);SocialTests.Command(state,a.CharacterId,SocialAction.Invite,target:b.CharacterId);SocialTests.Command(state,b.CharacterId,SocialAction.Accept,invite:Assert.Single(state.Invitations(b.CharacterId)));SocialTests.Command(state,a.CharacterId,SocialAction.Create,SocialKind.Guild,name:"Durable Guild");
        var party=state.Group(b.CharacterId,SocialKind.Party)!.Id;var handle=state.Identity(b.CharacterId).Handle;
        await social.SaveSocialCheckpointAsync([new(a,a.State),new(b,b.State)],null,new(lease,state.Dirty.ToArray()),default);await a.DisposeAsync();await b.DisposeAsync();await lease.DisposeAsync();
        store=create();await store.InitializeAsync(default);lease=await ((ISocialStore)store).OpenSocialAsync(default);state=new(()=>200000);state.Restore(lease.Rows);b=await store.OpenAsync(token,initial,default);
        Assert.Equal(party,state.Group(b.CharacterId,SocialKind.Party)!.Id);Assert.Equal(handle,state.Identity(b.CharacterId).Handle);Assert.Empty(state.Invitations(b.CharacterId));Assert.Equal(1UL,state.Identity(b.CharacterId).LastOperation);
        state.SetOnline(a.CharacterId);SocialTests.Command(state,a.CharacterId,SocialAction.Kick,target:b.CharacterId);await ((ISocialStore)store).SaveSocialCheckpointAsync([],null,new(lease,state.Dirty.ToArray()),default);await b.DisposeAsync();await lease.DisposeAsync();
        lease=await ((ISocialStore)store).OpenSocialAsync(default);state=new(()=>300000);state.Restore(lease.Rows);Assert.Null(state.Group(b.CharacterId,SocialKind.Party));Assert.NotNull(state.Group(a.CharacterId,SocialKind.Guild));await lease.DisposeAsync();
    }
    internal static async Task Fence(Func<ICharacterStore> create)
    {
        var store=create();await store.InitializeAsync(default);var initial=PvpTests.Initial(PvpTests.World());var a=await store.OpenAsync("",initial,default);var token=a.IssuedToken;var social=(ISocialStore)store;var lease=await social.OpenSocialAsync(default);
        var state=new SocialSimulation(()=>100000);state.Restore(lease.Rows);state.SetOnline(a.CharacterId);SocialTests.Command(state,a.CharacterId,SocialAction.Create);
        await social.SaveSocialCheckpointAsync([],null,new(lease,[]),default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>social.SaveSocialCheckpointAsync([new(a,a.State with{Health=1})],null,new(lease,state.Dirty.ToArray()),default));await a.DisposeAsync();await lease.DisposeAsync();
        a=await store.OpenAsync(token,initial,default);Assert.Equal(initial.Health,a.State.Health);await a.DisposeAsync();lease=await social.OpenSocialAsync(default);Assert.Empty(lease.Rows);await lease.DisposeAsync();
    }
    [LivePostgresFact]public Task PostgresMembershipAndReceiptsSurviveRestart()=>Postgres(RoundTrip);
    [LivePostgresFact]public Task PostgresStaleRealmFenceRollsBackCharacterAndMembership()=>Postgres(Fence);
    [Fact]public async Task SchemaSevenMigrationPreservesCharacterEquipmentPvpAndWorld()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var initial=PvpTests.Initial(PvpTests.World()) with {Progression=PvpTests.Initial(PvpTests.World()).Progression! with{Pvp=new(){Mode=PvpMode.Voluntary,Reputation=-10,Pk=2}}};var a=await store.OpenAsync("",initial,default);var token=a.IssuedToken;var id=a.CharacterId;await a.DisposeAsync();
        var world=await store.OpenWorldAsync("migration_social",new(){Repairs=1},default);await world.DisposeAsync();
        using(var c=WorldNodeStorageTests.Connect(store.DatabasePath))using(var q=c.CreateCommand()){q.CommandText="DROP TABLE social_members;DROP TABLE social_names;DROP TABLE social_records;DELETE FROM schema_version WHERE version=8";q.ExecuteNonQuery();}
        await store.InitializeAsync(default);a=await store.OpenAsync(token,initial,default);Assert.Equal(id,a.CharacterId);Assert.Equal(initial.Serialize(),a.State.Serialize());await a.DisposeAsync();world=await store.OpenWorldAsync("migration_social",new(),default);Assert.Equal(1,world.State.Repairs);await world.DisposeAsync();await using var social=await store.OpenSocialAsync(default);Assert.Empty(social.Rows);
    }
    [Fact]public Task SqliteUniqueMembershipAndNameFailuresRollBackEntireBatch(){var path=new SqliteCharacterStore().DatabasePath;return Unique(()=>new SqliteCharacterStore(path));}
    [LivePostgresFact]public Task PostgresUniqueMembershipAndNameFailuresRollBackEntireBatch()=>Postgres(Unique);
    private static async Task Unique(Func<ICharacterStore> create)
    {
        var store=create();await store.InitializeAsync(default);var initial=PvpTests.Initial(PvpTests.World());await using var a=await store.OpenAsync("",initial,default);await using var b=await store.OpenAsync("",initial with{Inventory=SavedInventory.Empty},default);var social=(ISocialStore)store;var lease=await social.OpenSocialAsync(default);
        var duplicate=new[]{new DatabaseSocialRow("g_1",2,1,"{}","",[a.CharacterId]),new DatabaseSocialRow("g_2",2,2,"{}","",[a.CharacterId])};
        await Assert.ThrowsAnyAsync<Exception>(()=>social.SaveSocialCheckpointAsync([new(a,a.State with{Health=1})],null,new(lease,duplicate),default));
        duplicate=[new("g_1",3,1,"{}","SAME NAME",[a.CharacterId]),new("g_2",3,2,"{}","SAME NAME",[b.CharacterId])];await Assert.ThrowsAnyAsync<Exception>(()=>social.SaveSocialCheckpointAsync([],null,new(lease,duplicate),default));
        await lease.DisposeAsync();await using var check=await social.OpenSocialAsync(default);Assert.Empty(check.Rows);
    }
    [LivePostgresFact]public Task PostgresSchemaSevenMigrationPreservesCharacterEquipmentPvpAndWorld()=>PostgresCore(async(create,connection)=>
    {
        var store=create();await store.InitializeAsync(default);var w=PvpTests.World();var initial=PvpTests.Initial(w);var a=await store.OpenAsync("",initial,default);var token=a.IssuedToken;var id=a.CharacterId;await a.DisposeAsync();var world=await store.OpenWorldAsync("migration_social",new(){Repairs=1},default);await world.DisposeAsync();
        await using(var c=new Npgsql.NpgsqlConnection(connection)){await c.OpenAsync();await using var q=new Npgsql.NpgsqlCommand("DROP TABLE social_members;DROP TABLE social_names;DROP TABLE social_records;DELETE FROM project_g_schema WHERE version=8",c);await q.ExecuteNonQueryAsync();}
        await store.InitializeAsync(default);a=await store.OpenAsync(token,initial,default);Assert.Equal(id,a.CharacterId);Assert.Equal(initial.Serialize(),a.State.Serialize());await a.DisposeAsync();world=await store.OpenWorldAsync("migration_social",new(),default);Assert.Equal(1,world.State.Repairs);await world.DisposeAsync();await using var social=await ((ISocialStore)store).OpenSocialAsync(default);Assert.Empty(social.Rows);
    });
    private static Task Postgres(Func<Func<ICharacterStore>,Task> test)=>PostgresCore((create,_)=>test(create));
    private static async Task PostgresCore(Func<Func<ICharacterStore>,string,Task> test)
    {
        var source=Environment.GetEnvironmentVariable("PROJECT_G_TEST_POSTGRES")!;
        var name="social_test_"+Guid.NewGuid().ToString("N");
        await using var admin=new Npgsql.NpgsqlConnection(source);await admin.OpenAsync();
        await using(var command=new Npgsql.NpgsqlCommand("CREATE DATABASE "+name,admin))await command.ExecuteNonQueryAsync();
        var connection=new Npgsql.NpgsqlConnectionStringBuilder(source){Database=name,Pooling=false}.ToString();
        var sources=new List<PostgresCharacterStore>();ICharacterStore Create(){var db=new PostgresCharacterStore(connection);sources.Add(db);return new DatabaseCharacterStore(db);}
        try{await test(Create,connection);}finally{foreach(var db in sources)await db.DisposeAsync();await using var command=new Npgsql.NpgsqlCommand("DROP DATABASE "+name+" WITH (FORCE)",admin);await command.ExecuteNonQueryAsync();}
    }
}
