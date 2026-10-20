using System.Text.Json.Nodes;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server;

public sealed class ExperiencePersistenceTests
{
    [Fact]public void FractionModelsRejectInvalidVersionNonCanonicalAndOversizedOrNullState()
    {
        foreach(var bad in new[]{new SavedExperienceFraction(2,"1","2"),new(1,"2","2"),new(1,"1","0"),new(1,"2","4"),
            new(1,"01","2"),new(1,"-1","2"),new(1,"1e0","2"),new(1,"1",new string('9',513))})
            Assert.Throws<InvalidDataException>(bad.Validate);
        var w=StarterZoneTests.World();var initial=w.CreateInitialCharacter().Progression!;
        var json=JsonNode.Parse(initial.Serialize())!;json["ExperienceFraction"]=null;
        Assert.Throws<InvalidDataException>(()=>SavedProgression.Deserialize(json.ToJsonString()));
        var fraction=new SavedExperienceFraction(1,"1","7");var saved=initial with {ExperienceFraction=fraction};
        Assert.Equal(fraction,SavedProgression.Deserialize(saved.Serialize()).ExperienceFraction);
        Assert.Null(SavedProgression.Deserialize(initial.Serialize()).ExperienceFraction);
        var map=w.CreateInitialCharacter().Progression!.Exploration??new SavedExploration
            {RegionKey="prototype",Width=1,Height=1,OriginX=0,OriginZ=0,CellSize=1,Cells=[1],Tutorial=0};
        Assert.Throws<InvalidDataException>(()=>(map with {ExperienceCells=2}).Validate());
    }

    [Fact]public void ValidRepeatedCraftKeepsPositiveRewardAndRetryAfterReconnectDoesNotRepeatIt()
    {
        var w=CraftingTests.World();w.ConfigureExperienceRewards(new(1,1,20,40,100,5));
        w.AddPlayer(42,new(1),CraftingTests.Materials(w,new SavedMaterial(2,4)));
        Assert.Equal(CraftOutcome.Accepted,CraftingTests.Do(w,42,1,CraftAction.Make,1));
        ProfessionTests.Step(w,24);Assert.Equal(CraftOutcome.Accepted,CraftingTests.Do(w,42,2,CraftAction.Make,1));
        var saved=w.CaptureCharacter(42);Assert.Equal(4,saved.Progression!.Experience);
        Assert.Equal(2,Assert.Single(saved.Inventory!.Crafting!.Materials).Quantity);
        w.RemovePlayer(42);w.AddPlayer(42,new(2),saved);
        Assert.Equal(CraftOutcome.AlreadyProcessed,CraftingTests.Do(w,42,2,CraftAction.Make,1));
        Assert.Equal(4,w.CaptureCharacter(42).Progression!.Experience);
    }

    [Fact]public void RewardConfigurationAndZeroCraftExperienceFailAtLoad()
    {
        foreach(var bad in new[]{new ExperienceRewardsDefinition(2,1,20,40,100,5),new(1,0,20,40,100,5),new(1,1,0,40,100,5),
            new(1,1,20,float.NaN,100,5),new(1,1,20,40,0,5),new(1,1,20,40,100,0)})
            Assert.Throws<InvalidDataException>(bad.Validate);
        var catalog=Content.Tests.Server.Data.ContentCatalogTests.Load();var crafting=catalog.Crafting!;
        Assert.Throws<InvalidDataException>(()=>(crafting with {Recipes=[crafting.Recipes[0] with {Experience=0}]}).Validate(catalog));
    }
}
