using System.Text.Json.Nodes;
using Content.Server.Activities;
using Content.Server.Stats;
using Content.Server.Progression;
using Content.Tests.Server.Data;
using ProjectG.Balance;
using Xunit;

namespace Content.Tests.Server;
public sealed class CharacterStatsAuditTests
{
    [Fact]public void ApprovedHealthWeightsGiveFivePerStrengthAndTenPerVitalityAtBaseHundred()
    {
        var c=ContentCatalogTests.Load();var calculator=new StatCalculator(c.Balance);var hero=c.Creatures["test_adventurer"];
        Assert.Equal(.05,c.Balance.Health.Strength);Assert.Equal(.1,c.Balance.Health.Vitality);
        Assert.Equal(100,calculator.Calculate(hero).MaxHealth,8);
        Assert.Equal(925,calculator.Calculate(hero with {Stats=new(165,0,0,0,0,0,0)}).MaxHealth,8);
        Assert.Equal(1750,calculator.Calculate(hero with {Stats=new(0,0,165,0,0,0,0)}).MaxHealth,8);
        Assert.Equal(460,calculator.Calculate(hero with {Stats=new(24,24,24,24,23,23,23)}).MaxHealth,8);
        Assert.Contains("+5 HP/STR",EditorSchema.Describe(new("health","Рост HP",[],"balance/health"),"balance/health/strength").Help);
    }
    [Fact]public async Task PreviousHealthMaximumRestoresItsFractionAndReopensUnderNewBalance()
    {
        var world=CampActivityTests.World();var initial=world.CreateInitialCharacter();
        var previous=initial with {Stats=new(20,0,0,0,0,0,0),Health=52,ResourceMaxima=new(1,104,100,100),
            Progression=initial.Progression! with {StatPoints=0,Allocation=initial.Progression!.Allocation! with {Invested=new(20,0,0,0,0,0,0)}}};
        var store=new Content.Tests.Server.Persistence.SqliteCharacterStore();await store.InitializeAsync(default);string token;
        await using(var lease=await store.OpenAsync("",previous,default))
        {
            token=lease.IssuedToken;world.AddPlayer(1,new(1),lease.State);var next=world.CaptureCharacter(1);
            Assert.Equal(100,next.Health,8);Assert.Equal(200,next.ResourceMaxima!.Health,8);
            Assert.Equal(lease.State.Progression!.StatPoints,next.Progression!.StatPoints);
            await store.SaveAsync([new(lease,next)],default);
        }
        await using var restored=await store.OpenAsync(token,initial,default);
        Assert.Equal(100,restored.State.Health,8);Assert.Equal(200,restored.State.ResourceMaxima!.Health,8);
    }
    [Fact]public void WeightsSpendEveryPointAndKeepOnlyAssignedStats()
    {
        foreach(var points in new[]{0,18,63,113,163,513,1013})
        foreach(var weights in new[]{new[]{1,1,1,1,1,1,1},new[]{3,2,1,0,1,1,0},new[]{0,0,0,0,0,0,1}})
        {var values=CharacterStatsAudit.Allocate(points,weights);Assert.Equal(points,values.Sum());for(var i=0;i<7;i++)if(weights[i]==0)Assert.Equal(0,values[i]);}
    }
    [Fact]public void AuditDoesNotRaiseAlphaCapAndShowsNegativeDefenseWithoutNegativeDamage()
    {
        var c=ContentCatalogTests.Load();var cases=new CharacterAuditCases(1,[1,30],[100,200],"test_creature","training_sword",100,
            [new("INT",[0,0,0,1,0,0,0]),new("WIS",[0,0,0,0,0,0,1])],[new("naked",[])],
            [new("none",new()),new("negative",new(){CriticalChance=1,CriticalDamage=-10,PhysicalDefense=-500})],new(){Scale=150,Offset=4});
        var report=CharacterStatsAudit.Run(c,new(1,2,20,40,100,5){OreExperience=1,WatchExperience=20},CampActivityTests.Definition(),cases,"content","cases");
        Assert.Equal(30,c.Progression.LevelCap);Assert.Equal(16,report.Rows.Count);
        Assert.All(report.Rows,r=>{Assert.True(double.IsFinite(r.PhysicalEffectiveHealth));Assert.True(r.BasicDps>=0);Assert.Equal(r.Level>30,r.Stress);});
        Assert.All(report.Rows.Where(r=>r.Modifier=="negative"),r=>{Assert.Equal(0,r.BasicDps);Assert.Null(r.StationaryTtk);Assert.True(r.PhysicalMitigation<0);});
        var intRows=report.Rows.Where(r=>r.Build=="INT"&&r.Modifier=="none").ToArray();Assert.Equal(intRows[0].Stats.MaxMana,intRows[^1].Stats.MaxMana);
        Assert.True(report.Rows.Single(r=>r.Level==30&&r.Build=="WIS"&&r.Modifier=="none").Stats.MaxMana>intRows[1].Stats.MaxMana);
        Assert.Equal(30,report.Curves[0].Thresholds.Count);Assert.Equal(0,report.Curves[0].Thresholds[^1]);
    }
    [Fact]public void RewardDraftUndoValidationRevisionAndDiskConflictPreserveSource()
    {
        var directory=Path.Combine(Path.GetTempPath(),"reward-editor-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"rewards.json");
        try
        {
            File.WriteAllText(path,"{\"version\":1,\"revision\":2,\"pveExperience\":20,\"pveRadius\":40,\"explorationCells\":100,\"explorationExperience\":5,\"oreExperience\":1,\"watchExperience\":20}");
            var doc=new ExperienceRewardDocument(path);var initial=File.ReadAllText(path);
            Assert.Throws<InvalidDataException>(()=>doc.Apply(doc.Value with {PveExperience=-1}));Assert.False(doc.Dirty);
            doc.Apply(doc.Value with {PveExperience=21});Assert.Equal(initial,File.ReadAllText(path));doc.Undo();Assert.False(doc.Dirty);doc.Redo();doc.Save();
            Assert.Equal(3,ExperienceRewardsDefinition.LoadFile(path).Revision);Assert.Equal(21,doc.Value.PveExperience);
            doc.Apply(doc.Value with {OreExperience=2});File.AppendAllText(path," ");var changed=File.ReadAllText(path);
            Assert.Throws<IOException>(doc.Save);Assert.Equal(changed,File.ReadAllText(path));Assert.True(doc.Dirty);
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact]public void WorkshopBasicPreviewMatchesZeroDamageOfNegativeCriticalMultiplier()
    {
        var root=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        root["items"]!.AsArray().Single(i=>i!["id"]!.GetValue<string>()=="arena_training_sword")!["modifiers"]=JsonNode.Parse("{\"criticalChance\":1,\"criticalDamage\":-10}");
        var c=Content.Server.Data.ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        Assert.Equal(0,BalancePreview.Calculate(c,new(){TargetId="test_creature"}).ExpectedDps);
    }
}
