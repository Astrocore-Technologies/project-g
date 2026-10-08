using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server;

public sealed class FrozenCharacterStateTests
{
    [Fact]
    public void CargoKeepsResourcesEquipmentProgressionAndAllThreeEchoesWithoutArrayAliasing()
    {
        var world = PvpTests.World(); var initial = PvpTests.Initial(world);
        var state = initial with
        {
            Health = 17, Mana = 3, AttackCooldownSeconds = 4,
            Cooldowns = [new("test", 7)],
            Echoes = new() { Active = Enumerable.Range(1, 3).Select(i => new SavedEcho(
                Guid.NewGuid(), "test_guardian_echo", (byte)i, i, -i, i + 0.5, i + 2.5)).ToArray() }
        };
        var character = state.Serialize(); var items = state.Inventory!.Serialize();
        var echoes = state.Echoes!.Serialize(); var progression = state.Progression!.Serialize();
        var frozen = FrozenCharacterState.Capture(state);
        state.Cooldowns[0] = new("changed", 0);
        state.Inventory.Items[0] = new(Guid.NewGuid(), "changed", EquipmentSlot.None);
        state.Echoes.Active[0] = state.Echoes.Active[0] with { InstanceId = Guid.NewGuid(), SignatureCooldownSeconds = 0 };
        state.Progression.Skills[0] = state.Progression.Skills[0] with { Level = 99 };
        var restored = frozen.Restore();
        Assert.Equal(character, restored.Serialize());
        Assert.Equal(items, restored.Inventory!.Serialize());
        Assert.Equal(echoes, restored.Echoes!.Serialize());
        Assert.Equal(progression, restored.Progression!.Serialize());
        restored.Echoes.Active[0] = restored.Echoes.Active[0] with { SignatureCooldownSeconds = 0 };
        Assert.Equal(echoes, frozen.Restore().Echoes!.Serialize());
    }

    [Fact]
    public void LegacyAbsentComponentsAreNotSilentlyBootstrappedByTransfer()
    {
        var initial = PvpTests.World().CreateInitialCharacter() with { Inventory = null, Echoes = null, Progression = null };
        var restored = FrozenCharacterState.Capture(initial).Restore();
        Assert.Null(restored.Inventory); Assert.Null(restored.Echoes); Assert.Null(restored.Progression);
        Assert.Equal(initial.Serialize(), restored.Serialize());
    }

    [Fact]
    public void InvalidCompanionCargoFailsBeforeItCanBeReserved()
    {
        var state = PvpTests.World().CreateInitialCharacter() with
        { Echoes = new() { Active = [new(Guid.Empty, "test_guardian_echo", 1, 0, 0)] } };
        Assert.Throws<InvalidDataException>(() => FrozenCharacterState.Capture(state));
    }
}
