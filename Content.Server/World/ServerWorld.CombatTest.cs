using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Development;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    /// <summary>Host-only setup before login; never callable through a gameplay packet.</summary>
    public void ConfigureCombatTestArena(CombatTestSetup setup)
    {
        if (RegionId != "linvel_beech" || Players.Count != 0 || _developmentBalanceCharacter is not null)
            throw new InvalidOperationException("Combat test arena requires the empty Beech Valley starting region.");
        var catalog = _progressionCatalog!;
        var initial = new BalanceTestBuild { Stats = catalog.Creatures["test_adventurer"].Stats, ProfessionId = 2 }
            .CreateCharacter(catalog, CreateInitialCharacter());
        var skills = initial.Progression!.Skills.Select(s => s with { Slot = (byte)(Array.IndexOf(setup.Bar, s.DefinitionId) + 1) }).ToArray();
        if (setup.Bar.Any(id => !skills.Any(s => s.DefinitionId == id))) throw new InvalidDataException("Unknown test bar skill.");
        var calculator = new StatCalculator(catalog.Balance);
        var echo = catalog.Echoes["test_guardian_echo"];
        var playerStats = calculator.Calculate(catalog.Creatures["test_adventurer"]);
        foreach (var id in setup.Kit.Take(1).Concat(setup.Kit.Where(id => id == "test_armor_item")))
            playerStats = catalog.Items[id].Modifiers.Apply(playerStats);
        var state = initial with
        {
            Health = playerStats.MaxHealth, Mana = playerStats.MaxMana,
            Inventory = new SavedInventory { Items = setup.Kit.Select((id, i) => new SavedItem(Guid.NewGuid(), id,
                i == 0 ? EquipmentSlot.Weapon : id == "test_armor_item" ? EquipmentSlot.Armor : EquipmentSlot.None)).ToArray() },
            Progression = initial.Progression with { Skills = skills },
            // The existing character-creation transaction persists this bootstrap document.
            // Login normalizes these explicit QA grants to Ready; the first checkpoint writes owned rows.
            Echoes = new SavedEchoes { Active = Enumerable.Range(1, 3)
                .Select(slot => new SavedEcho(Guid.NewGuid(), echo.Id, (byte)slot, null, null)).ToArray() }
        };
        state.Validate(); state.Progression!.Validate(); ValidateProfession(state.Progression);
        state.Echoes!.Validate();
        _developmentBalanceCharacter = state;
        foreach (var entry in setup.Actors)
        {
            var position = new Vector2(entry.X, entry.Z);
            var height = LocateHeight(position);
            if (!Navigation.IsOnSurface(new(position.X, height, position.Y))) throw new InvalidDataException($"Obstructed test anchor '{entry.Key}'.");
            var options = new NpcOptions { Enabled = true, DefinitionId = entry.Profile, SpawnId = entry.Key,
                X = entry.X, Z = entry.Z, Height = height, RespawnSeconds = 5, DevelopmentPassive = entry.Passive,
                AggroRadius = entry.Passive ? .1f : 5, LeashRadius = entry.Passive ? 20 : 12, Speed = entry.Boss ? 2.5f : 3 };
            var kind = entry.Boss ? CombatEntityKind.Boss : CombatEntityKind.Monster;
            var id = AllocateEntityId(); Combat!.Add(id, position, kind, entry.Profile, height: height);
            _spatial.Add(id, position); Abilities!.AddActor(id, catalog.Creatures[entry.Profile]);
            var boss = entry.Boss ? new BossOptions { Actor = options } : null;
            var actor = new NpcSimulation(id, Combat, _spatial, Navigation, _movement, options, _interest.CellSize,
                boss is null ? null : catalog.Abilities[boss.AreaAbilityId], boss, calculator, Abilities);
            RegisterNpcSpawn(actor, entry.Key, kind);
        }
    }
}
