using System.Numerics;
using Content.Server.Combat;
using Content.Server.Development;
using Content.Server.Regions;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Configuration;
using ProjectG.Balance;
using Xunit;

namespace Content.Tests.Server;

public sealed class BalanceToolsTests
{
    [Fact]public void EchoRefusalAuthoringIsExplicitValidatedAtomicAndUndoable()
    {
        var document=new BalanceDocument(ContentCatalogTests.DataPath);var path=document.Entries().Single(e=>e.Name=="echoes / test_guardian_echo").Path;
        var original=document.Text;
        Assert.Throws<ArgumentException>(()=>document.ConfigureEchoRefusal(path,5,null));Assert.Equal(original,document.Text);Assert.False(document.CanUndo);
        Assert.Throws<ArgumentException>(()=>document.ConfigureEchoRefusal(path,4,Content.Server.Data.EchoRefusalActionReaction.FinishCurrent));Assert.Equal(original,document.Text);
        document.ConfigureEchoRefusal(path,5,Content.Server.Data.EchoRefusalActionReaction.InterruptAsEnemy);
        var echo=document.Catalog.Echoes["test_guardian_echo"];Assert.Equal((byte)5,echo.Stars);
        Assert.Equal(Content.Server.Data.EchoRefusalActionReaction.InterruptAsEnemy,echo.RefusalDuringAction);
        var page=EditorSchema.Pages(document).Single(p=>p.Key=="echo/test_guardian_echo");
        Assert.DoesNotContain(EditorSchema.Fields(document,page),field=>field.Path.EndsWith("/stars"));
        document.Undo();Assert.Equal(original,document.Text);Assert.False(document.Dirty);
        document.Redo();Assert.Equal((byte)5,document.Catalog.Echoes["test_guardian_echo"].Stars);
        document.ConfigureEchoRefusal(path,4,null);Assert.Null(document.Catalog.Echoes["test_guardian_echo"].RefusalDuringAction);
        document.ConfigureEchoRefusal(path,null,null);Assert.Equal(original,document.Text);
        Assert.Equal(original,new BalanceDocument(ContentCatalogTests.DataPath).Text);
    }
    [Fact]
    public void DraftUndoAndSavePreserveUntouchedPrecisionAndDetectExternalEdit()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.Copy(ContentCatalogTests.DataPath, path);
            var document = new BalanceDocument(path);
            var coefficient = document.Catalog.Balance.HealthRecovery.Vitality;
            Assert.Contains(document.Fields("balance"), field => field.Path == "balance/meleeAttack/agility" && field.Value == 0);
            document.Set("balance/meleeAttack/agility", .5);
            Assert.Equal(.5, document.Catalog.Balance.MeleeAttack.Agility);
            document.Undo(); Assert.False(document.Dirty);
            document.Set("weapons/0/attack", 20);
            Assert.Equal(20, document.Catalog.Weapons["training_sword"].Attack);
            document.Undo(); Assert.False(document.Dirty);
            document.Redo(); Assert.True(document.Dirty);
            document.SaveSource();
            Assert.Equal(coefficient, new BalanceDocument(path).Catalog.Balance.HealthRecovery.Vitality);
            document.Set("weapons/0/attack", 30);
            var external = File.ReadAllText(path) + "\n"; File.WriteAllText(path, external);
            Assert.Throws<IOException>(document.SaveSource);
            Assert.Equal(external, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidDraftCannotReplaceSource()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.Copy(ContentCatalogTests.DataPath, path); var original = File.ReadAllBytes(path);
            var document = new BalanceDocument(path); document.Set("weapons/0/attack", -1);
            Assert.Throws<InvalidDataException>(document.SaveSource); Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SandboxBuildLoadsInRealWorldAndMatchesServerStats(int profession)
    {
        var catalog = ContentCatalogTests.Load();
        var world = TerrainIntegrationTests.CurrentWorlds(catalog).StartingWorld;
        var authoredSpawn = world.CreateInitialCharacter();
        var build = new BalanceTestBuild { Level = 15, Stats = new(40, 10, 20, 1, 5, 5), ProfessionId = (ushort)profession };
        world.ConfigureBalanceSandbox(build);
        var initial = world.CreateInitialCharacter();
        var next = world.CreateInitialCharacter();
        Assert.NotEqual(initial.Inventory!.Items[0].InstanceId, next.Inventory!.Items[0].InstanceId);
        var player = world.AddPlayer(1, new PlayerId(1), initial);
        var preview = BalancePreview.Calculate(catalog, build);
        var actor = world.Combat!.Get(player.EntityId);
        Assert.Equal(preview.Stats, actor.Stats);
        Assert.Equal(preview.Interval, actor.AttackInterval, 8);
        Assert.Equal(profession, world.ProfessionState(player.EntityId, 0).ActiveId);
        Assert.Equal(15, world.ProgressionState(player.EntityId, 0).Level);
        Assert.Equal(new Vector2(authoredSpawn.X, authoredSpawn.Z), player.Position);
        Assert.Equal(authoredSpawn.Surface!.Height, player.Height);
        Assert.Equal(initial.Stats, world.CaptureCharacter(1).Stats);
        if (profession == 2)
        {
            Assert.Equal(8, world.ProgressionState(player.EntityId, 0).Skills.Count(s => s.Slot > 0));
            Assert.Equal(3, world.Abilities!.Loadout(player.EntityId, 0).Abilities.Single(a => a.Form == AbilityForm.Dash).Range);
        }
    }

    [Fact]
    public void TechniquePreviewUsesExecutionFormulaAndRespondsToBalanceEdit()
    {
        var document = new BalanceDocument(ContentCatalogTests.DataPath); var build = new BalanceTestBuild { ProfessionId = 2 };
        var before = BalancePreview.TechniqueHit(document.Catalog, build, "sword_thrust");
        document.Set("abilities/6/melee/damageFactor", 2.4);
        Assert.Equal(before * 2, BalancePreview.TechniqueHit(document.Catalog, build, "sword_thrust"), 8);
        Assert.Equal(12, CombatBalanceMath.TechniquePower(10, new() { DamageFactor = 1.2 }, 1), 8);
    }
}
