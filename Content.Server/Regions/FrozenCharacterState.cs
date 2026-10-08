using System.Text;
using Content.Server.Persistence;

namespace Content.Server.Regions;

/// <summary>
/// Immutable server-only transfer cargo. All four persistent documents are captured
/// together; retaining a CharacterState record alone would alias its mutable arrays.
/// Runtime entity IDs and client credentials never enter this cargo.
/// </summary>
public sealed class FrozenCharacterState
{
    private readonly string character;
    private readonly string? inventory;
    private readonly string? echoes;
    private readonly string? progression;

    private FrozenCharacterState(CharacterState state)
    {
        character = Bounded(state.Serialize());
        inventory = state.Inventory is null ? null : Bounded(state.Inventory.Serialize());
        echoes = state.Echoes is null ? null : Bounded(state.Echoes.Serialize());
        progression = state.Progression is null ? null : Bounded(state.Progression.Serialize());
    }

    public static FrozenCharacterState Capture(CharacterState state) => new(state);

    public CharacterState Restore() => CharacterState.Deserialize(character) with
    {
        Inventory = inventory is null ? null : SavedInventory.Deserialize(inventory),
        Echoes = echoes is null ? null : SavedEchoes.Deserialize(echoes),
        Progression = progression is null ? null : SavedProgression.Deserialize(progression)
    };

    private static string Bounded(string document)
    {
        if (Encoding.UTF8.GetByteCount(document) > 8192)
            throw new InvalidDataException("Regional transfer document exceeds the persistence budget.");
        return document;
    }
}
