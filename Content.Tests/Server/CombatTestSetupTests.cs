using Content.Server.Configuration;
using Content.Server.Development;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class CombatTestSetupTests
{
    [Fact]
    public void FreshCharactersOwnIndependentKitsEchoesAndAllCombatForms()
    {
        var (catalog, setup) = CombatTestSetup.Load(Path.Combine(AppContext.BaseDirectory, "Data", "prototype.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "Development", "combat-test.json"));
        var world = new ServerWorld(Options.Create(new MovementOptions { MinX = -500, MaxX = 500, MinZ = -500, MaxZ = 500, SpawnX = -425, SpawnZ = 330 }),
            Options.Create(new InterestOptions()), navigation: Options.Create(new NavigationOptions { CellSize = 20 }), catalog: catalog, inventory: Options.Create(new InventoryOptions { Enabled = true }),
            echoes: Options.Create(new EchoOptions { Enabled = true }), regionId: "linvel_beech");
        world.ConfigureCombatTestArena(setup);
        var first = world.CreateInitialCharacter(); var second = world.CreateInitialCharacter();
        first.Validate(); first.Progression!.Validate(); first.Inventory!.Validate();
        Assert.Equal(8, first.Inventory.Items.Length); Assert.Equal(3, first.Echoes!.Active.Length);
        Assert.Empty(first.Inventory.Items.Select(i => i.InstanceId).Intersect(second.Inventory!.Items.Select(i => i.InstanceId)));
        Assert.Empty(first.Echoes.Active.Select(e => e.InstanceId).Intersect(second.Echoes!.Active.Select(e => e.InstanceId)));
        Assert.All(Enum.GetValues<CombatActionForm>(), form => Assert.Contains(first.Progression.Skills, s => catalog.Abilities[s.DefinitionId].Action.Form == form));
        Assert.Equal(8, first.Progression.Skills.Count(s => s.Slot != 0));
        Assert.Equal(20, first.Progression.StatPoints);
        var player = world.AddPlayer(1, new(1), first);
        for (var i = 0; i < 20; i++) world.Simulate(.05f);
        Assert.Equal(0, world.Combat!.CombatRemaining(player.EntityId));
        var owned = world.CaptureCharacter(1).OwnedEchoes!;
        Assert.Equal(3, owned.Count);
        Assert.All(owned, e => Assert.Equal(Content.Server.Persistence.SavedEchoAvailability.Ready, e.Availability));
        Assert.Equal(3, setup.Actors.Count(a => a.Passive));
    }
}
