using System.Numerics;
using Content.Server.Persistence;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private NetworkEntityId _swordTrainer;
    private readonly HashSet<NetworkEntityId> _arenaDummies = new();
    private readonly Dictionary<NetworkEntityId, double> _brokenDummies = new();
    public NetworkEntityId SwordTrainerId => _swordTrainer;
    public IReadOnlyCollection<NetworkEntityId> ArenaDummies => _arenaDummies;

    private void InitializeSwordTraining(IReadOnlyDictionary<string, float[]>? placements)
    {
        if (_progressionCatalog?.Swordsman is not { } s || RegionId != s.RegionId || Inventory is null) return;
        Vector2 Point(string name)
        {
            if (placements is null || !placements.TryGetValue(name, out var p) || p.Length != 2 || !Navigation.IsWalkable(new(p[0], p[1])))
                throw new InvalidDataException($"Missing or obstructed arena scene marker: {name}");
            return new(p[0], p[1]);
        }
        var trainerPosition = Point("SwordTrainer");
        var trainer = s.Trainer with { X = trainerPosition.X, Z = trainerPosition.Y };
        _swordTrainer = AllocateEntityId(); _questNpcs.Add(_swordTrainer, trainer); _spatial.Add(_swordTrainer, trainerPosition);
        _arenaDummies.Add(TrainingTargetId);
        foreach (var marker in s.DummyAnchors)
        {
            var position = Point(marker);
            if (marker == "SwordTarget") continue;
            var id = AllocateEntityId(); Combat!.Add(id, position, CombatEntityKind.TrainingTarget, s.DummyDefinitionId);
            _spatial.Add(id, position); _arenaDummies.Add(id);
        }
    }

    private void RecordSwordTraining()
    {
        if (_arenaDummies.Count == 0) return;
        var s = _progressionCatalog!.Swordsman!;
        // Capture equipment at the instant of an ordinary hit, before this tick's equip commands execute.
        foreach (var hit in Combat!.Events)
        {
            if (hit.Damage <= 0 || !_arenaDummies.Contains(hit.TargetId) ||
                !_progression.TryGetValue(hit.AttackerId, out var value) || value.SwordTraining is not { } training ||
                training.Damage >= s.RequiredDamage || !Inventory!.HasEquippedItem(hit.AttackerId, s.TrainingItemId)) continue;
            _progression[hit.AttackerId] = value with { SwordTraining = training with { Damage = Math.Min(s.RequiredDamage, training.Damage + hit.Damage) } };
            DirtyProgression(hit.AttackerId); _professionDirty.Add(hit.AttackerId);
        }
    }

    private void ResetArenaDummies()
    {
        // The authored arena has a small validated bound (2–8), not a global actor scan.
        foreach (var id in _arenaDummies)
        {
            if (Combat!.Get(id).Health > 0) continue;
            if (!_brokenDummies.TryGetValue(id, out var at)) _brokenDummies[id] = Combat.Time + _progressionCatalog!.Swordsman!.DummyResetSeconds;
            else if (Combat.Time >= at) { Combat.Respawn(id); _brokenDummies.Remove(id); }
        }
    }

    private bool NearSwordTrainer(NetworkEntityId id)
    {
        if (!_swordTrainer.IsValid || !_playersByEntity.TryGetValue(id, out var player)) return false;
        var position = _questNpcs[_swordTrainer].Position;
        var range = _progressionCatalog!.Swordsman!.InteractionRange;
        return !player.Motion.IsMoving && !player.Motion.IsDashing && !Inventory!.IsTrading(id) &&
            Vector2.DistanceSquared(player.Position, position) <= range * range && Navigation.CanTraverse(player.Position, position);
    }

    private void TalkSwordTrainer(NetworkEntityId id, QuestCommand command)
    {
        var s = _progressionCatalog!.Swordsman!; var value = _progression[id];
        var actor = Combat!.Get(id);
        var outcome = actor.Health <= 0 ? QuestOutcome.InvalidState : actor.IsCasting || Inventory!.IsTrading(id) ? QuestOutcome.Busy
            : !NearSwordTrainer(id) ? QuestOutcome.TooFar : QuestOutcome.Accepted;
        if (outcome == QuestOutcome.Accepted && command.Action == QuestAction.TrainingSword)
        {
            if (!Inventory!.GrantTrainingItem(id, s.TrainingItemId)) outcome = QuestOutcome.MaterialFull;
            else if (value.SwordTraining is null)
            {
                value = value with { SwordTraining = new(1, 0) }; _progression[id] = value;
                DirtyProgression(id); _professionDirty.Add(id);
            }
        }
        else if (command.Action is not QuestAction.Talk) outcome = QuestOutcome.Unavailable;
        var trained = value.SwordTraining?.Damage >= s.RequiredDamage;
        var already = value.Profession.ActiveId == s.ProfessionId || value.Profession.RetiredIds.Contains(s.ProfessionId);
        // Completion alone is not an offer. The player must return and actually talk to the trainer.
        if (outcome == QuestOutcome.Accepted && trained && !already && value.Profession.OfferedId != s.ProfessionId)
        {
            value = value with { Profession = value.Profession with { OfferedId = s.ProfessionId } };
            _progression[id] = value; DirtyProgression(id); _professionDirty.Add(id);
        }
        var owns = Inventory!.HasItem(id, s.TrainingItemId);
        var text = already ? "Продолжай тренироваться. Навыки и восемь ячеек панели — в окне K; описание пассивок — в окне P."
            : trained ? "Основы освоены. Предлагаю стать Мечником: 10 приёмов, 5 пассивок и длинный рывок. Решение за тобой; переход подтверждается отдельно."
            : $"Нанеси манекенам {s.RequiredDamage:0} урона обычными ударами тренировочного меча. Зачтено: {Math.Floor(value.SwordTraining?.Damage ?? 0):0}/{s.RequiredDamage:0}. " +
                (owns ? "Надень меч в инвентаре I. После тренировки вернись ко мне." : "Возьми у меня меч и надень его в инвентаре I.");
        byte choices = outcome != QuestOutcome.Accepted ? (byte)0 : (byte)((owns ? 0 : 4) | (trained && !already ? 8 : 0));
        _questReplies[id] = new(command.Sequence, Tick, command.NpcId, outcome, choices, s.Trainer.Name, text);
    }
}
