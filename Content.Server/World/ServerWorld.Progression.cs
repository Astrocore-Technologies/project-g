using System.Numerics;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Server.Stats;
using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private ContentCatalog? _progressionCatalog;
    private Vector2[] DiscoveryLandmarks = [new(-9,-6),new(9,6)];
    private readonly Dictionary<NetworkEntityId,SavedProgression> _progression = new();
    private readonly Dictionary<NetworkEntityId,ProgressionCommand> _progressionPending = new();
    private readonly Dictionary<NetworkEntityId,(uint Sequence,uint Tick)> _progressionSequences = new();
    private readonly HashSet<NetworkEntityId> _progressionDirty = new();
    private readonly Dictionary<NetworkEntityId,ProgressionResult> _progressionResults = new();
    private readonly Dictionary<NetworkEntityId,StatPreview> _statPreviews = new();
    private sealed record AllocationQuote(uint Sequence, BaseStats Basis, DerivedStats Current, DerivedStats Projected,
        int AvailablePoints, int[] Allocation);
    private readonly Dictionary<NetworkEntityId,AllocationQuote> _allocationQuotes = new();
    public IReadOnlyDictionary<NetworkEntityId,StatPreview> StatPreviews => _statPreviews;
    public IReadOnlyDictionary<NetworkEntityId,ProgressionResult> ProgressionResults => _progressionResults;
    public bool IsProgressionDirty(NetworkEntityId id) => _progressionDirty.Contains(id);
    public void ClearProgressionResults() { _progressionDirty.Clear(); _progressionResults.Clear(); _statPreviews.Clear(); }
    private void AddProgression(NetworkEntityId id, SavedProgression? saved)
    {
        if (_progressionCatalog is not { } catalog) return;
        var value = saved ?? SavedProgression.Starter(_playerDefinition!,catalog);
        // A pre-progression save can have edited stats; do not invent its historical investments.
        if (saved is null && _playersByEntity[id].BaseStats != _playerDefinition!.Stats)
            value = value with { Allocation = null };
        // Add this single new learned technique to existing swordsmen, preserving all levels and bar slots.
        if (catalog.Abilities.ContainsKey("sword_rising") && catalog.Swordsman is { } swordsman &&
            (value.Profession.ActiveId == swordsman.ProfessionId || value.Profession.RetiredIds.Contains(swordsman.ProfessionId)) &&
            !value.Skills.Any(s => s.DefinitionId == "sword_rising"))
            value = value with { Skills = [..value.Skills,new SavedSkill("sword_rising",1,0,0)] };
        value.Validate(); ValidateProfession(value);
        value.Allocation?.ValidateStats(_playersByEntity[id].BaseStats);
        if(value.DeliveryQuest is { } quest && quest.DefinitionId!=catalog.DeliveryQuest?.Id) throw new InvalidDataException("Quest content migration required.");
        if(value.SwordTraining is { } training && (catalog.Swordsman is not { } sword || training.Damage > sword.RequiredDamage)) throw new InvalidDataException("Sword training content migration required.");
        var balance = catalog.Progression;
        if (value.Allocation is { } ledger && ledger.EarnedFromLevels != balance.GrantedPoints(value.Level))
            throw new InvalidDataException("Saved point grants require an explicit balance migration.");
        if (value.ExperienceCurveKey is { } key ? key != catalog.ExperienceCurve.Key : balance.ExperienceCurve is not null)
            throw new InvalidDataException("Saved experience curve requires an explicit migration.");
        if (value.Level > balance.LevelCap || (value.Level == balance.LevelCap ? value.Experience != 0 : value.Experience >= catalog.ExperienceCurve.NextLevel(value.Level)))
            throw new InvalidDataException("Saved level curve requires migration.");
        foreach (var skill in value.Skills)
        {
            if (!catalog.Abilities.TryGetValue(skill.DefinitionId,out var definition) ||
                (!_playerDefinition!.AbilityIds.Contains(skill.DefinitionId) && definition.NetworkId != balance.DiscoverySkillId && !HasProfessionSkillSource(value,skill.DefinitionId)) ||
                !catalog.SkillProgressions[skill.DefinitionId].Accepts(skill.Level, skill.Practice) ||
                (definition.Kind is AbilityKind.Dash or AbilityKind.Passive && skill.Slot != 0))
                throw new InvalidDataException($"Saved skill '{skill.DefinitionId}' level/practice/source requires content migration.");
        }
        if (value.Skills.Count(s => catalog.Abilities[s.DefinitionId].Kind == AbilityKind.Dash) > 1 ||
            value.Skills.Any(s => catalog.Abilities[s.DefinitionId].NetworkId == balance.DiscoverySkillId && value.Discoveries == 0))
            throw new InvalidDataException("Invalid progression source.");
        _progression.Add(id,value); Combat!.SetProfession(id,value.Profession.ActiveId); Abilities!.ApplyProgression(id,value); _progressionDirty.Add(id); _professionDirty.Add(id); EvaluateProfessionOffer(id);
    }
    public ProgressionState ProgressionState(NetworkEntityId id, uint tick)
    {
        var value = _progression[id]; var catalog = _progressionCatalog!; var balance = catalog.Progression;
        var stats = _playersByEntity[id].BaseStats;
        var skills = value.Skills.Select(s => new SkillProgress(catalog.Abilities[s.DefinitionId].NetworkId,s.Level,s.Practice,
            catalog.SkillProgressions[s.DefinitionId].PracticeThreshold(s.Level),s.Slot,false,catalog.Abilities[s.DefinitionId].Passive is not null)).ToList();
        if (skills.Count < NetworkConstants.MaxLearnedSkills && !skills.Any(s => s.Id == balance.DiscoverySkillId)) skills.Add(new(balance.DiscoverySkillId,0,0,0,0,value.Discoveries != 0,
            catalog.Abilities.Values.Single(a=>a.NetworkId==balance.DiscoverySkillId).Passive is not null));
        return new(id,tick,value.Level,value.Experience,catalog.ExperienceCurve.NextLevel(value.Level),value.StatPoints,value.Discoveries,
            [stats.Strength,stats.Agility,stats.Vitality,stats.Intelligence,stats.Dexterity,stats.Luck,stats.Wisdom],skills.ToArray());
    }
    public bool HasProgression => _progressionCatalog is not null;
    public bool TryQueueProgression(int connectionId, ProgressionCommand command)
    {
        if (!_playersByConnection.TryGetValue(connectionId,out var player) || !_progression.ContainsKey(player.EntityId) ||
            !NetworkProtocol.ValidProgressionCommand(command)) return false;
        var id = player.EntityId;
        if (_progressionSequences.TryGetValue(id,out var last))
        {
            if (!MovementSimulation.IsSequenceNewer(command.Sequence,last.Sequence)) return false;
            if (last.Tick == Tick)
            {
                _progressionSequences[id] = (command.Sequence,Tick); _progressionPending.Remove(id);
                _progressionResults[id] = new(command.Sequence,Tick,ProgressionOutcome.RateLimited); return false;
            }
        }
        if (command.Action != ProgressionAction.PreviewStats) GroundItems?.CancelChannel(id,Tick);
        _progressionSequences[id] = (command.Sequence,Tick);
        _progressionPending[id] = command with { Allocation = command.Allocation is null ? null : (int[])command.Allocation.Clone() }; return true;
    }
    private void GrantExperience(NetworkEntityId id, int amount)
    {
        if (!_progression.TryGetValue(id,out var value) || amount <= 0 || value.Level == _progressionCatalog!.Progression.LevelCap) return;
        var balance = _progressionCatalog.Progression;
        var award = ProgressionRules.Award(_progressionCatalog.ExperienceCurve, balance.StatPointsPerLevel,
            value.Level, value.Experience, amount);
        var next = value with { Level = award.Level, Experience = award.Experience,
            StatPoints = checked(value.StatPoints + award.GrantedStatPoints),
            Allocation = award.GrantedStatPoints != 0 && value.Allocation is { } allocation
                ? allocation with { EarnedFromLevels = checked(allocation.EarnedFromLevels + award.GrantedStatPoints) } : value.Allocation };
        // The other components were validated on load. Do not rebuild skill/reference sets on each damage reward.
        if (next.StatPoints > 100000) throw new InvalidOperationException("Available points exceed the current wire/storage representation.");
        next.Allocation?.Validate(next.StatPoints);
        _progression[id] = next; DirtyProgression(id);
    }
    private void DirtyProgression(NetworkEntityId id) { _progressionDirty.Add(id); MarkPersistent(id); }
    // Only moving players visit landmarks; persistent bits make rewards one-time across reconnects.
    private void VisitDiscoveries(ServerPlayer player)
    {
        if (!_progression.TryGetValue(player.EntityId,out var value) || Combat!.Get(player.EntityId).Health <= 0) return;
        var landmarks = DiscoveryLandmarks;
        for (var i=0;i<landmarks.Length;i++)
        {
            var bit = (byte)(1 << i);
            if ((value.Discoveries & bit) != 0 || Vector2.DistanceSquared(player.Position,landmarks[i]) > 2.25f) continue;
            _progression[player.EntityId] = value with { Discoveries = (byte)(value.Discoveries | bit) };
            if (HasStarterZone) _starterDirty.Add(player.EntityId);
            GrantExperience(player.EntityId,_progressionCatalog!.Progression.DiscoveryExperience); DirtyProgression(player.EntityId);
            value = _progression[player.EntityId];
            EvaluateProfessionOffer(player.EntityId);
        }
    }
    private void SimulateProgression()
    {
        if (_progressionCatalog is not { } catalog) return;
        // Combat events are created exactly once by the authoritative simulation; no client reward RPC.
        foreach (var action in Combat!.Events) if (action.Damage > 0 && IsPlayer(action.AttackerId) && !IsPlayer(action.TargetId) && !_arenaDummies.Contains(action.TargetId)) GrantExperience(action.AttackerId,1);
        // Periodic ticks are not separate actions and must not multiply experience with server tick rate.
        foreach(var hit in Abilities!.DirectHits)
            if (hit.Damage > 0 && IsPlayer(hit.ActorId) && !IsPlayer(hit.TargetId) && !_arenaDummies.Contains(hit.TargetId)) GrantExperience(hit.ActorId,1);
        foreach (var use in Abilities.Practice)
        {
            RecordProfessionUse(use.ActorId);
            if (!_progression.TryGetValue(use.ActorId,out var value)) continue;
            var index = Array.FindIndex(value.Skills,s => catalog.Abilities[s.DefinitionId].NetworkId == use.SkillId);
            if (index < 0) continue;
            var skill = value.Skills[index]; var curve = catalog.SkillProgressions[skill.DefinitionId];
            if (skill.Level == curve.LevelCap) continue;
            var (level, practice) = curve.AwardPractice(skill.Level, skill.Practice);
            var skills = (SavedSkill[])value.Skills.Clone(); skills[index] = skill with { Level = level, Practice = practice };
            _progression[use.ActorId] = value with { Skills = skills };
            if (level != skill.Level) Abilities.ApplyProgression(use.ActorId,_progression[use.ActorId]);
            DirtyProgression(use.ActorId);
        }
        foreach (var (id,command) in _progressionPending)
        {
            if (command.Action == ProgressionAction.PreviewStats)
            {
                var previewOutcome = PreviewAllocation(id,command);
                _progressionResults[id] = new(command.Sequence,Tick,previewOutcome);
                continue;
            }
            var actor = Combat.Get(id); var value = _progression[id]; var outcome = actor.Health <= 0 ? ProgressionOutcome.InvalidState
                : command.Action==ProgressionAction.AssignSlot && Combat.InCombat(id) || actor.IsCasting || Abilities.HasActiveEffects(id) ? ProgressionOutcome.Busy : ProgressionOutcome.Accepted;
            if (outcome == ProgressionOutcome.Accepted)
            {
                if (command.Action is ProgressionAction.AllocateStat or ProgressionAction.AllocateStats)
                {
                    var allocation = command.Allocation ?? new int[7];
                    if (command.Action == ProgressionAction.AllocateStat) allocation[command.StatIndex] = 1;
                    var total = allocation.Sum();
                    if (value.StatPoints < total) outcome = ProgressionOutcome.NoPoints;
                    else if (command.PreviewSequence != 0 && !MatchesAllocationQuote(id, command, allocation))
                    { _allocationQuotes.Remove(id); outcome = ProgressionOutcome.StalePreview; }
                    else
                    {
                        var next = value with { StatPoints = value.StatPoints - total,
                            Allocation = value.Allocation is { } ledger
                                ? ledger with { Invested = ledger.Invested.Add(allocation) } : null };
                        next.Validate();
                        var player = _playersByEntity[id]; var old = player.BaseStats;
                        double[] stats = [old.Strength,old.Agility,old.Vitality,old.Intelligence,old.Dexterity,old.Luck,old.Wisdom];
                        for (var i = 0; i < 7; i++) stats[i] += allocation[i];
                        player.BaseStats = new(stats[0],stats[1],stats[2],stats[3],stats[4],stats[5],stats[6]);
                        try
                        {
                            if (Inventory is not null) Inventory.RefreshStats(id);
                            else
                            { var profile = Combat.PrepareEquipment(player.BaseStats,[]); var resources = Abilities.PrepareEquipment(id,Combat.WithEffects(id,profile.Stats,profile.Weapon)); Combat.ApplyEquipment(id,profile); Abilities.ApplyEquipment(id,resources); }
                            _progression[id] = next;
                            _allocationQuotes.Remove(id);
                        }
                        catch (ArgumentException) { player.BaseStats = old; outcome = ProgressionOutcome.InvalidState; }
                    }
                }
                else if (command.Action == ProgressionAction.LearnSkill)
                {
                    var definition = catalog.Abilities.Values.FirstOrDefault(a => a.NetworkId == command.SkillId);
                    if (command.SkillId != catalog.Progression.DiscoverySkillId || value.Discoveries == 0 || definition is null ||
                        value.Skills.Length >= NetworkConstants.MaxLearnedSkills || value.Skills.Any(s => s.DefinitionId == definition.Id)) outcome = ProgressionOutcome.Unavailable;
                    else _progression[id] = value with { Skills = [..value.Skills,new SavedSkill(definition.Id,1,0,0)] };
                }
                else
                {
                    var index = Array.FindIndex(value.Skills,s => catalog.Abilities[s.DefinitionId].NetworkId == command.SkillId);
                    if (index < 0 || catalog.Abilities[value.Skills[index].DefinitionId].Kind is AbilityKind.Dash or AbilityKind.Passive) outcome = ProgressionOutcome.Unavailable;
                    else
                    {
                        var skills = (SavedSkill[])value.Skills.Clone();
                        for (var i=0;i<skills.Length;i++) if (i != index && command.Slot != 0 && skills[i].Slot == command.Slot) skills[i] = skills[i] with { Slot = 0 };
                        skills[index] = skills[index] with { Slot = command.Slot }; _progression[id] = value with { Skills = skills };
                    }
                }
                if (outcome == ProgressionOutcome.Accepted) { Abilities.ApplyProgression(id,_progression[id]); DirtyProgression(id); }
            }
            _progressionResults[id] = new(command.Sequence,Tick,outcome);
        }
        _progressionPending.Clear();
    }
    private void RemoveProgression(NetworkEntityId id)
    { RemoveProfession(id); _progression.Remove(id); _progressionPending.Remove(id); _progressionSequences.Remove(id); _progressionDirty.Remove(id); _progressionResults.Remove(id); _statPreviews.Remove(id); _allocationQuotes.Remove(id); }
    private ProgressionOutcome PreviewAllocation(NetworkEntityId id, ProgressionCommand command)
    {
        if (command.Allocation!.Sum() > _progression[id].StatPoints) return ProgressionOutcome.NoPoints;
        var stats = _playersByEntity[id].BaseStats; var a = command.Allocation!;
        var proposed = new BaseStats(stats.Strength+a[0],stats.Agility+a[1],stats.Vitality+a[2],stats.Intelligence+a[3],stats.Dexterity+a[4],stats.Luck+a[5],stats.Wisdom+a[6]);
        try
        {
            var projected = ProjectStats(id,proposed);
            var current = Combat!.Get(id).Stats;
            _allocationQuotes[id] = new(command.Sequence, stats, current, projected, _progression[id].StatPoints, (int[])a.Clone());
            _statPreviews[id] = new(command.Sequence,Tick,DisplayStats(current),DisplayStats(projected));
            return ProgressionOutcome.Accepted;
        }
        catch (ArgumentException) { return ProgressionOutcome.InvalidState; }
    }
    private DerivedStats ProjectStats(NetworkEntityId id, BaseStats stats) =>
        Inventory?.PreviewStats(id,stats) ?? Combat!.WithEffects(id,Combat.PrepareEquipment(stats,[]).Stats);
    private bool MatchesAllocationQuote(NetworkEntityId id, ProgressionCommand command, int[] allocation)
    {
        if (!_allocationQuotes.TryGetValue(id, out var quote) || quote.Sequence != command.PreviewSequence ||
            quote.Basis != _playersByEntity[id].BaseStats || quote.Current != Combat!.Get(id).Stats ||
            quote.AvailablePoints != _progression[id].StatPoints || !allocation.AsSpan().SequenceEqual(quote.Allocation)) return false;
        var basis = quote.Basis;
        var proposed = new BaseStats(basis.Strength+allocation[0],basis.Agility+allocation[1],basis.Vitality+allocation[2],
            basis.Intelligence+allocation[3],basis.Dexterity+allocation[4],basis.Luck+allocation[5],basis.Wisdom+allocation[6]);
        return quote.Projected == ProjectStats(id,proposed);
    }
    private static double[] DisplayStats(DerivedStats s) => [s.MeleeAttack,s.RangedAttack,s.MagicAttack,s.MeleeWeaponMultiplier,s.RangedWeaponMultiplier,
        s.PhysicalDefense,s.MagicDefense,s.MaxHealth,Math.Max(0,s.MaxMana),s.HealthRecovery,s.ManaRecovery,s.HealthItemMultiplier,s.ManaItemMultiplier,
        s.AttackSpeedMultiplier,s.CastSpeedMultiplier,s.CriticalChance,s.BlockDamage,s.Accuracy,s.Evasion,s.CriticalDamage,s.BlockStrength,Math.Clamp(s.CooldownReduction,0,1),Math.Max(0,s.MaxStamina),s.StaminaRecovery];
}
