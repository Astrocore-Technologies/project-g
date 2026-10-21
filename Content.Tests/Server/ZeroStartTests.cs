using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Development;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class ZeroStartTests
{
    private static ServerWorld World(ContentCatalog? catalog=null)=>new(Options.Create(new MovementOptions()),
        Options.Create(new InterestOptions()),catalog:catalog??ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);

    [Fact]public async Task ZeroStartAndFullStrengthInvestmentRemainDurableWithoutFreeRefillsOrExtraPoints()
    {
        var world=World();var initial=world.CreateInitialCharacter();var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        Assert.Equal(default,initial.Stats);Assert.Equal(100,initial.Health);Assert.Equal(100,initial.Mana);
        Assert.Equal(20,initial.Progression!.StatPoints);Assert.Equal(default,initial.Progression.Allocation!.StartingStats);
        var player=world.AddPlayer(1,new(1),initial with {Health=50,Mana=50});
        Assert.Equal(100,world.Combat!.Get(player.EntityId).Stats.MaxStamina);
        Assert.True(world.TryQueueProgression(1,new(1,ProgressionAction.AllocateStats,0,0,0,[20,0,0,0,0,0,0])));
        world.Simulate(.05f);var saved=world.CaptureCharacter(1);
        Assert.Equal(new BaseStats(20,0,0,0,0,0,0),saved.Stats);Assert.Equal(0,saved.Progression!.StatPoints);
        Assert.Equal(200,saved.ResourceMaxima!.Health,8);Assert.Equal(100,saved.ResourceMaxima.Mana);
        Assert.Equal((50+(double).05f)/100*200,saved.Health,8);
        var session=await store.OpenAsync("",saved,default);
        await store.SaveAsync([new(session,saved)],default);var token=session.IssuedToken;await session.DisposeAsync();
        await using var reopened=await store.OpenAsync(token,initial,default);
        Assert.Equal(saved.Stats,reopened.State.Stats);Assert.Equal(saved.Progression.Allocation,reopened.State.Progression!.Allocation);
        var restored=World();restored.AddPlayer(2,new(2),reopened.State);
        Assert.Equal(0,restored.CaptureCharacter(2).Progression!.StatPoints);
    }

    [Fact]public async Task PreviousEighteenPointLedgerIsPreservedAndRequiresExplicitMigration()
    {
        var oldData=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        oldData["progression"]!["initialStatPoints"]=18;
        var hero=oldData["creatures"]!.AsArray().Single(c=>c!["id"]!.GetValue<string>()=="test_adventurer")!;
        foreach(var key in new[]{"strength","agility","vitality","intelligence","dexterity","luck","wisdom"})hero["stats"]![key]=10;
        hero["baseMana"]=60;
        var old=World(ContentCatalog.Parse(Encoding.UTF8.GetBytes(oldData.ToJsonString()))).CreateInitialCharacter();
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var first=await store.OpenAsync("",old,default);var token=first.IssuedToken;
        var before=first.State.Serialize();await first.DisposeAsync();
        var world=World();await using var reopened=await store.OpenAsync(token,world.CreateInitialCharacter(),default);
        Assert.Contains("explicit balance migration",Assert.Throws<InvalidDataException>(()=>world.AddPlayer(1,new(1),reopened.State)).Message);
        Assert.Equal(before,reopened.State.Serialize());Assert.Equal(18,reopened.State.Progression!.StatPoints);
        Assert.Equal(new BaseStats(10,10,10,10,10,10,10),reopened.State.Stats);
    }

    [Fact]public void NewWorkshopFixtureUsesTheSameZeroStatsAndTwentyPointBudget()
    {
        var catalog=ContentCatalogTests.Load();var initial=World(catalog).CreateInitialCharacter();
        var fixture=new BalanceTestBuild();var state=fixture.CreateCharacter(catalog,initial);
        Assert.Equal(default,fixture.Stats);Assert.Equal(default,state.Stats);
        Assert.Equal(20,state.Progression!.StatPoints);Assert.NotNull(state.Progression.Allocation);
        Assert.Equal(100,state.Health);Assert.Equal(100,state.Mana);state.Validate();
    }
}
