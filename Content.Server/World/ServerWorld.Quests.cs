using System.Numerics;
using Content.Server.Quests;
using Content.Server.Persistence;
using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private DeliveryQuestDefinition? _deliveryQuest;
    private readonly Dictionary<NetworkEntityId, QuestNpcDefinition> _questNpcs = new();
    private NetworkEntityId _questGiver, _questRecipient;
    private readonly Dictionary<NetworkEntityId, QuestCommand> _questPending = new();
    private readonly Dictionary<NetworkEntityId, (uint Sequence, uint Tick)> _questSequences = new();
    private readonly Dictionary<NetworkEntityId, QuestReply> _questReplies = new();
    public bool HasQuests => _deliveryQuest is not null || _swordTrainer.IsValid;
    public IReadOnlyDictionary<NetworkEntityId, QuestReply> QuestReplies => _questReplies;
    private void InitializeQuests(bool enabled)
    {
        if (!enabled) return;
        _deliveryQuest = _progressionCatalog?.DeliveryQuest ?? throw new InvalidDataException("Missing delivery quest content.");
        if (Inventory is null) throw new InvalidDataException("Delivery quests require inventory.");
        if (_deliveryQuest.RegionId != RegionId) return;
        foreach (var npc in new[] { _deliveryQuest.Giver, _deliveryQuest.Recipient })
        {
            if (!Navigation.IsWalkable(npc.Position)) throw new InvalidDataException($"Quest NPC {npc.Name} is not on walkable ground.");
            var id = AllocateEntityId(); _questNpcs.Add(id, npc); _spatial.Add(id, npc.Position);
            if (npc == _deliveryQuest.Giver) _questGiver = id; else _questRecipient = id;
        }
    }
    public bool TryGetQuestNpc(NetworkEntityId id, out QuestNpcSpawn spawn)
    {
        spawn = default; if (!_questNpcs.TryGetValue(id, out var npc)) return false;
        spawn = new(id, Tick, npc.Position, npc.Name, npc.Role); return true;
    }
    public bool TryQueueQuest(int connection, QuestCommand command)
    {
        if (!HasQuests || !_playersByConnection.TryGetValue(connection, out var player) || !NetworkProtocol.ValidQuestCommand(command)) return false;
        var id = player.EntityId;
        if (_questSequences.TryGetValue(id, out var previous))
        {
            if (!MovementSimulation.IsSequenceNewer(command.Sequence, previous.Sequence)) return false;
            if (previous.Tick == Tick) return false;
        }
        _questSequences[id] = (command.Sequence, Tick); _questPending[id] = command; return true;
    }
    public QuestJournal QuestJournal(NetworkEntityId id)
    {
        var saved = _progression[id].DeliveryQuest;
        if (saved is null) return new(id, Tick, QuestStatus.Unknown, "Поручения", "Принятых поручений пока нет. Поговорите с жителями пристани.", "—", 0, 0, 0);
        var definition = _deliveryQuest!;
        var material = _progressionCatalog!.Crafting!.Materials.Single(m => m.Id == definition.MaterialId).Name;
        return new(id, Tick, saved.Status, definition.Title, definition.Objective, material, definition.Quantity, Inventory!.QuestMaterialCount(id, definition.MaterialId), definition.Experience);
    }
    private void SimulateQuests()
    {
        if (!HasQuests) return;
        var q = _deliveryQuest;
        foreach (var (id, c) in _questPending)
        {
            if (c.NpcId == _swordTrainer && _swordTrainer.IsValid) { TalkSwordTrainer(id, c); continue; }
            if (q is null) continue;
            var player = _playersByEntity[id]; var actor = Combat!.Get(id); var saved = _progression[id].DeliveryQuest;
            var npc = _questNpcs.GetValueOrDefault(c.NpcId); var outcome = QuestOutcome.Accepted;
            if (c.Action != QuestAction.Journal)
            {
                if (actor.Health <= 0) outcome = QuestOutcome.InvalidState;
                else if (npc is null) outcome = QuestOutcome.Unavailable;
                else if (actor.IsCasting || player.Motion.IsDashing || player.Motion.IsMoving || Inventory!.IsTrading(id)) outcome = QuestOutcome.Busy;
                else if (Vector2.DistanceSquared(player.Position, npc.Position) > q.InteractionRange * q.InteractionRange || !Navigation.CanTraverse(player.Position, npc.Position)) outcome = QuestOutcome.TooFar;
                else if (c.Action == QuestAction.Accept)
                {
                    if (c.NpcId != _questGiver) outcome = QuestOutcome.Unavailable;
                    else if (saved is not null) outcome = QuestOutcome.AlreadyProcessed;
                    else if ((outcome = Inventory!.ChangeQuestMaterial(id, q.MaterialId, q.Quantity)) == QuestOutcome.Accepted)
                    { _progression[id] = _progression[id] with { DeliveryQuest = new(1, q.Id, QuestStatus.Active) }; DirtyProgression(id); }
                }
                else if (c.Action == QuestAction.Deliver)
                {
                    if (c.NpcId != _questRecipient || saved is null) outcome = QuestOutcome.Unavailable;
                    else if (saved.Status == QuestStatus.Completed) outcome = QuestOutcome.AlreadyProcessed;
                    else if ((outcome = Inventory!.ChangeQuestMaterial(id, q.MaterialId, -q.Quantity)) == QuestOutcome.Accepted)
                    {
                        _progression[id] = _progression[id] with { DeliveryQuest = saved with { Status = QuestStatus.Completed } };
                        GrantExperience(id, q.Experience); DirtyProgression(id);
                    }
                }
                else if (c.Action != QuestAction.Talk) outcome = QuestOutcome.Unavailable;
            }
            saved = _progression[id].DeliveryQuest;
            // Replies expose this conversation only. Journal and XP are published after the common commit barrier.
            var text = saved?.Status == QuestStatus.Completed ? q.Thanks
                : saved is not null ? (c.NpcId == _questRecipient ? q.RecipientReminder : q.Reminder)
                : c.NpcId == _questGiver ? q.Offer : q.RecipientGreeting;
            byte choices = 0;
            if (outcome == QuestOutcome.Accepted && c.Action != QuestAction.Journal)
                choices = (byte)(saved is null && c.NpcId == _questGiver ? 1 : saved?.Status == QuestStatus.Active && c.NpcId == _questRecipient ? 2 : 0);
            _questReplies[id] = new(c.Sequence, Tick, c.NpcId, outcome, choices, npc?.Name ?? "Журнал", text);
        }
        _questPending.Clear();
    }
    public void ClearQuestResults() => _questReplies.Clear();
    private void ResetQuestSession(NetworkEntityId id)
    { _questPending.Remove(id); _questSequences.Remove(id); _questReplies.Remove(id); }
}
