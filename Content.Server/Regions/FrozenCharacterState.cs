using System.Text;
using Content.Server.Persistence;

namespace Content.Server.Regions;

/// <summary>
/// Immutable server-only transfer cargo. Persistent documents and effect rows are captured
/// together; retaining a CharacterState record alone would alias its mutable arrays.
/// Runtime entity IDs and client credentials never enter this cargo.
/// </summary>
public sealed class FrozenCharacterState
{
    private readonly string character;
    private readonly string? inventory;
    private readonly string? echoes;
    private readonly string? progression;
    private readonly Content.Database.DatabaseEffectRow[]? effects;
    private readonly Content.Database.DatabaseEchoRow[]? ownedEchoes;
    private readonly Content.Database.DatabaseDeathLogRow[]? deaths;
    private readonly string? journal;

    private FrozenCharacterState(CharacterState state)
    {
        character = Bounded(state.Serialize());
        inventory = state.Inventory is null ? null : Bounded(state.Inventory.Serialize());
        echoes = state.Echoes is null ? null : Bounded(state.Echoes.Serialize());
        progression = state.Progression is null ? null : Bounded(state.Progression.Serialize());
        effects = state.Effects is null ? null : CombatEffectDocuments.Serialize(state.Effects);
        ownedEchoes=state.OwnedEchoes is null?null:EchoCombatDocuments.Serialize(state.OwnedEchoes);
        if(effects is not null) foreach(var effect in effects) Bounded(effect.State);
        deaths=state.DeathLogs is null?null:DeathLogDocuments.Serialize(state.DeathLogs);
        journal=state.SessionJournal is null?null:DeathLogDocuments.SerializeSession(state.SessionJournal);
    }

    public static FrozenCharacterState Capture(CharacterState state) => new(state);

    public CharacterState Restore() => CharacterState.Deserialize(character) with
    {
        Inventory = inventory is null ? null : SavedInventory.Deserialize(inventory),
        Echoes = echoes is null ? null : SavedEchoes.Deserialize(echoes),
        Progression = progression is null ? null : SavedProgression.Deserialize(progression),
        Effects = effects is null ? null : CombatEffectDocuments.Deserialize(effects),
        OwnedEchoes=ownedEchoes is null?null:EchoCombatDocuments.Deserialize(ownedEchoes),
        DeathLogs=deaths is null?null:DeathLogDocuments.Deserialize(deaths),
        SessionJournal=journal is null?null:DeathLogDocuments.DeserializeSession(journal)
    };

    private static string Bounded(string document)
    {
        if (Encoding.UTF8.GetByteCount(document) > 8192)
            throw new InvalidDataException("Regional transfer document exceeds the persistence budget.");
        return document;
    }
}
