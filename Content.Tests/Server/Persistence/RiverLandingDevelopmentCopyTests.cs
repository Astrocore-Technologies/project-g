using Content.Database;
using Content.Server.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class DevelopmentCopyFactAttribute : FactAttribute
{
    public DevelopmentCopyFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROJECT_G_MIGRATION_SOURCE")))
            Skip = "Opt-in read-only source; set PROJECT_G_MIGRATION_SOURCE to test a backup of Development saves.";
    }
}

public sealed class RiverLandingDevelopmentCopyTests
{
    [DevelopmentCopyFact]
    public void EveryDevelopmentCharacterLoadsOnNewMapWithoutLosingProgress()
    {
        var source = Environment.GetEnvironmentVariable("PROJECT_G_MIGRATION_SOURCE")!;
        Assert.True(File.Exists(source));
        var folder = Path.Combine(AppContext.BaseDirectory, "sqlite-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "development-copy.db");
        new DevelopmentSqliteCharacterStore(source).CreateBackup(copy);
        var worlds = RiverLandingTests.Worlds();
        var world = worlds.Primary;
        var migration = new RiverLandingMigration(world.Navigation, new(-12, 18));
        using var connection = new SqliteConnection($"Data Source={copy};Pooling=False"); connection.Open();
        using (var worldRead = connection.CreateCommand())
        {
            worldRead.CommandText = "SELECT node_key, state, revision FROM world_nodes";
            using var rows = worldRead.ExecuteReader();
            while (rows.Read())
            {
                var owner = worlds.Worlds.SingleOrDefault(w => w.WorldNodeKey == rows.GetString(0));
                owner?.RestoreWorldNode(SavedWorldNode.Deserialize(rows.GetString(1)), rows.GetInt64(2));
            }
        }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.state, i.state, e.state, p.state FROM characters c JOIN character_inventory i USING(character_id) JOIN character_echoes e USING(character_id) LEFT JOIN character_progression p USING(character_id)";
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            var state = CharacterState.Deserialize(reader.GetString(0)) with
            { Inventory = SavedInventory.Deserialize(reader.GetString(1)), Echoes = SavedEchoes.Deserialize(reader.GetString(2)),
                Progression = reader.IsDBNull(3) ? null : SavedProgression.Deserialize(reader.GetString(3)) };
            var migrated = migration.Apply(state);
            Assert.Equal(state.Inventory.Serialize(), migrated.Inventory!.Serialize());
            Assert.Equal(state.Progression?.Level, migrated.Progression?.Level);
            Assert.Equal(state.Progression?.Experience, migrated.Progression?.Experience);
            Assert.Equal(state.Progression?.Profession, migrated.Progression?.Profession);
            Assert.Equal(state.Stats, migrated.Stats);
            Assert.Equal(state.Health, migrated.Health);
            var owner = worlds.Worlds.Single(w => w.RegionId == migrated.RegionId);
            owner.AddPlayer(1, new(1), migrated);
            var captured = owner.CaptureCharacter(1);
            Assert.Equal(migrated.Inventory.Items.Select(i => i.InstanceId), captured.Inventory!.Items.Select(i => i.InstanceId));
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(captured.Progression!.Serialize()) <= 8192);
            owner.RemovePlayer(1);
            count++;
        }
        Assert.True(count > 0, "No existing characters were verified.");
    }
}
