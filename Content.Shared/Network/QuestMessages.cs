using System.Numerics;
namespace Content.Shared.Network;

public enum QuestAction : byte { Talk, Accept, Deliver, Journal, TrainingSword }
public enum QuestStatus : byte { Unknown, Active, Completed }
public enum QuestOutcome : byte { Accepted, TooFar, Busy, InvalidState, Unavailable, AlreadyProcessed, MissingMaterials, MaterialFull, RateLimited }
public readonly record struct QuestCommand(uint Sequence, QuestAction Action, NetworkEntityId NpcId);
public readonly record struct QuestNpcSpawn(NetworkEntityId EntityId, uint ServerTick, Vector2 Position, string Name, string Role);
/// <summary>Only the currently available dialogue choices are disclosed.</summary>
public readonly record struct QuestReply(uint Sequence, uint ServerTick, NetworkEntityId NpcId, QuestOutcome Outcome, byte Choices, string Speaker, string Text);
public readonly record struct QuestJournal(NetworkEntityId OwnerId, uint ServerTick, QuestStatus Status, string Title,
    string Objective, string Material, ushort Required, ushort Carried, int Experience);
