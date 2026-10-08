using System.Security.Cryptography;
using Content.Server.Persistence;
using Content.Server.Professions;
using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.World;
public sealed partial class ServerWorld
{
    private readonly Dictionary<NetworkEntityId,ProfessionCommand> _professionPending = new();
    private readonly Dictionary<NetworkEntityId,(uint Sequence,uint Tick)> _professionSequences = new();
    private readonly Dictionary<NetworkEntityId,(ushort Id,ushort Previous,uint Token,uint At)> _professionConfirmations = new();
    private readonly Dictionary<NetworkEntityId,ProfessionResult> _professionResults = new();
    private readonly HashSet<NetworkEntityId> _professionDirty = new();
    public IReadOnlyDictionary<NetworkEntityId,ProfessionResult> ProfessionResults => _professionResults;
    public bool IsProfessionDirty(NetworkEntityId id) => _professionDirty.Contains(id);
    public void ClearProfessionResults() { _professionResults.Clear(); _professionDirty.Clear(); }
    private ProfessionDefinition? ProfessionDefinition(ushort id) => _progressionCatalog!.Professions.FirstOrDefault(p => p.Id == id);
    private static bool Eligible(SavedProgression value,ProfessionDefinition definition) =>
        (value.Discoveries & definition.DiscoveryMask) == definition.DiscoveryMask && value.Profession.SuccessfulUses >= definition.SuccessfulUses;
    private void ValidateProfession(SavedProgression value)
    {
        var profession=value.Profession; profession.Validate();
        foreach (var id in profession.RetiredIds.Append(profession.ActiveId))
            if (id != 0 && (ProfessionDefinition(id) is not { } source || !value.Skills.Any(s => s.DefinitionId == source.SkillId)))
                throw new InvalidDataException("Missing learned profession skill.");
        foreach (var id in profession.RetiredIds.Append(profession.ActiveId).Append(profession.OfferedId))
            if (id != 0 && (ProfessionDefinition(id) is not { } definition || !Eligible(value,definition)))
                throw new InvalidDataException("Saved profession/source requires migration.");
    }
    private bool HasProfessionSkillSource(SavedProgression value,string skill) => _progressionCatalog!.Professions.Any(p => p.SkillId == skill &&
        (value.Profession.ActiveId == p.Id || value.Profession.RetiredIds.Contains(p.Id)));
    private void EvaluateProfessionOffer(NetworkEntityId id)
    {
        var value=_progression[id]; var profession=value.Profession;
        if (profession.OfferedId != 0) return;
        foreach (var candidate in _progressionCatalog!.Professions)
        {
            if (candidate.Id == profession.ActiveId || profession.RetiredIds.Contains(candidate.Id) || !Eligible(value,candidate)) continue;
            _progression[id]=value with { Profession=profession with { OfferedId=candidate.Id } };
            DirtyProgression(id); _professionDirty.Add(id); return;
        }
    }
    private void RecordProfessionUse(NetworkEntityId id)
    {
        if (!_progression.TryGetValue(id,out var value) || value.Profession.SuccessfulUses == 10000) return;
        _progression[id]=value with { Profession=value.Profession with { SuccessfulUses=value.Profession.SuccessfulUses+1 } };
        DirtyProgression(id); EvaluateProfessionOffer(id);
    }
    public ProfessionState ProfessionState(NetworkEntityId id,uint tick)
    {
        var value=_progression[id].Profession;
        return new(id,tick,value.ActiveId,value.ActiveId == 0 ? "" : ProfessionDefinition(value.ActiveId)!.Name,
            value.OfferedId,value.OfferedId == 0 ? "" : ProfessionDefinition(value.OfferedId)!.Name);
    }
    public bool TryQueueProfession(int connectionId,ProfessionCommand command)
    {
        if (!_playersByConnection.TryGetValue(connectionId,out var player) || !_progression.ContainsKey(player.EntityId) || command.Sequence == 0 || !Enum.IsDefined(command.Action) ||
            (command.Action == ProfessionAction.Cancel ? command.ProfessionId != 0 || command.Confirmation != 0 : command.ProfessionId == 0 ||
            (command.Action == ProfessionAction.Prepare ? command.Confirmation != 0 : command.Confirmation == 0))) return false;
        var id=player.EntityId;
        if (_professionSequences.TryGetValue(id,out var last))
        {
            if (!MovementSimulation.IsSequenceNewer(command.Sequence,last.Sequence)) return false;
            if (last.Tick == Tick)
            {
                _professionSequences[id]=(command.Sequence,Tick); _professionPending.Remove(id); _professionConfirmations.Remove(id);
                _professionResults[id]=new(command.Sequence,Tick,ProfessionOutcome.RateLimited,0); return false;
            }
        }
        _professionSequences[id]=(command.Sequence,Tick); _professionPending[id]=command; return true;
    }
    private void SimulateProfessions()
    {
        foreach (var (id,command) in _professionPending)
        {
            var value=_progression[id]; var profession=value.Profession; var actor=Combat!.Get(id); uint token=0;
            var outcome=actor.Health <= 0 ? ProfessionOutcome.InvalidState : actor.IsCasting || Abilities!.HasActiveEffects(id) ? ProfessionOutcome.Busy : ProfessionOutcome.Accepted;
            if (command.Action == ProfessionAction.Cancel)
            { _professionConfirmations.Remove(id); outcome=ProfessionOutcome.Cancelled; }
            else if (outcome == ProfessionOutcome.Accepted)
            {
                var definition=ProfessionDefinition(command.ProfessionId);
                if (definition is null || profession.OfferedId != command.ProfessionId || profession.ActiveId == command.ProfessionId || profession.RetiredIds.Contains(command.ProfessionId) || !Eligible(value,definition)) outcome=ProfessionOutcome.Unavailable;
                else if (command.Action == ProfessionAction.Prepare)
                {
                    token=(uint)RandomNumberGenerator.GetInt32(1,int.MaxValue);
                    _professionConfirmations[id]=(command.ProfessionId,profession.ActiveId,token,Tick); outcome=ProfessionOutcome.Prepared;
                }
                else if (!_professionConfirmations.TryGetValue(id,out var confirmation) || confirmation.Token != command.Confirmation || confirmation.Id != command.ProfessionId || confirmation.Previous != profession.ActiveId || unchecked(Tick-confirmation.At) > 600)
                    outcome=ProfessionOutcome.InvalidConfirmation;
                else if (value.Skills.Length >= NetworkConstants.MaxLearnedSkills && !value.Skills.Any(s => s.DefinitionId == definition.SkillId)) outcome=ProfessionOutcome.Unavailable;
                else
                {
                    var retired=profession.ActiveId == 0 ? profession.RetiredIds : [..profession.RetiredIds,profession.ActiveId];
                    if (retired.Length > 8) outcome=ProfessionOutcome.Unavailable;
                    else
                    {
                        var skills=value.Skills.Any(s => s.DefinitionId == definition.SkillId) ? value.Skills : [..value.Skills,new SavedSkill(definition.SkillId,1,0,0)];
                        _progression[id]=value with { Profession=profession with { ActiveId=command.ProfessionId,OfferedId=0,RetiredIds=retired },Skills=skills };
                        Abilities!.ApplyProgression(id,_progression[id]); DirtyProgression(id); _professionDirty.Add(id);
                        EvaluateProfessionOffer(id);
                    }
                }
            }
            if (outcome != ProfessionOutcome.Prepared) _professionConfirmations.Remove(id);
            _professionResults[id]=new(command.Sequence,Tick,outcome,token);
        }
        _professionPending.Clear();
    }
    private void RemoveProfession(NetworkEntityId id)
    { _professionPending.Remove(id); _professionSequences.Remove(id); _professionConfirmations.Remove(id); _professionResults.Remove(id); _professionDirty.Remove(id); }
}
