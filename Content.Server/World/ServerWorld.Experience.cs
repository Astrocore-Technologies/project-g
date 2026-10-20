using System.Numerics;
using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private ExperienceRewardsDefinition? _experienceRewards;
    private sealed record PveVictory(string SourceId,Vector3 Position,bool Ordinary,CombatSourceId[] Characters,
        (CombatSourceId Source,CombatSourceId Owner)[] Echoes);
    private readonly List<PveVictory> _pveVictories=new();

    public void ConfigureExperienceRewards(ExperienceRewardsDefinition definition)
    {
        definition.Validate();
        if(_experienceRewards is not null || _playersByEntity.Count!=0 || Combat is null)
            throw new InvalidOperationException("Configure EXP rewards once before loading characters.");
        _experienceRewards=definition;Combat.Defeated+=QueuePveVictory;
    }

    private void QueuePveVictory(NetworkEntityId npc)
    {
        var actor=Combat!.Get(npc);
        if(actor.Kind is not (CombatEntityKind.Monster or CombatEntityKind.Boss))return;
        var encounter=Combat.Encounter(npc);
        if(actor.DeathId<=encounter.ExperienceDeathId)return;
        encounter.ExperienceDeathId=actor.DeathId;
        // Preserve source identities before corpse expiry/removal. Return-home already clears the ledger.
        _pveVictories.Add(new($"{actor.SourceIdentity.Id:N}:{actor.DeathId}",actor.Foot,actor.Kind==CombatEntityKind.Monster,
            encounter.Contribution.Entries.Keys.Where(s=>s.Kind==CombatSourceKind.Character).ToArray(),
            encounter.Contribution.Sources.Where(s=>s.Key.Kind==CombatSourceKind.Echo)
                .Select(s=>(s.Key,s.Value.Credit)).ToArray()));
    }

    private bool EligibleExperienceOwner(CombatSourceId source,Vector3 position,out NetworkEntityId id)
    {
        id=Combat!.ResolveSource(source);
        return source.Kind==CombatSourceKind.Character && _playersByEntity.TryGetValue(id,out var player) && player.Loaded &&
            player.ConnectionId>=0 && !Combat.Get(id).DetachedBody && Combat.Get(id).Health>0 &&
            Vector3.DistanceSquared(player.Foot,position)<=_experienceRewards!.PveRadius*_experienceRewards.PveRadius;
    }

    private void DistributePveExperience()
    {
        if(_experienceRewards is not {} definition)return;
        foreach(var victory in _pveVictories)
        {
            var people=new List<NetworkEntityId>();var echoes=new List<(NetworkEntityId Owner,Guid Instance)>();
            foreach(var source in victory.Characters)
                if(EligibleExperienceOwner(source,victory.Position,out var id) && _progression.TryGetValue(id,out var value) &&
                    value.Level<_progressionCatalog!.Progression.LevelCap)people.Add(id);
            foreach(var (source,credit) in victory.Echoes)
            {
                if(!EligibleExperienceOwner(credit,victory.Position,out var owner))continue;
                var id=Combat!.ResolveSource(source);
                if(Echoes?.TryGet(id,out var echo)!=true || echo.OwnerId!=owner || echo.InstanceId!=source.Id ||
                    Combat.Get(id).Health<=0 || !Combat.Get(id).Active ||
                    Vector3.DistanceSquared(echo.Motion.Foot,victory.Position)>definition.PveRadius*definition.PveRadius)continue;
                echoes.Add((owner,echo.InstanceId));
            }
            var recipients=people.Count+echoes.Count;if(recipients==0)continue;
            var reward=new ProgressionReward(ExperienceActivity.PveVictory,victory.SourceId,definition.Revision,definition.PveExperience);
            reward.Validate();
            var watch=victory.Ordinary&&_nodeState.CampWatch is {Active:true} active?active:null;
            var pool=checked(reward.Amount+(watch?.Experience??0));
            // Prepare the complete distribution before mutating any recipient; invalid storage must fail closed.
            var characters=people.Select(id=>(Id:id,State:PrepareExperience(_progression[id],pool,recipients))).ToArray();
            var companions=echoes.Select(e=>(e.Owner,State:Echoes!.PrepareExperience(e.Owner,e.Instance,pool,recipients))).ToArray();
            foreach(var (id,state) in characters){_progression[id]=state;DirtyProgression(id);}
            foreach(var (owner,state) in companions){Echoes!.ApplyExperience(owner,state);MarkPersistent(owner);}
            if(watch is not null)
            {
                _nodeState=_nodeState with {CampWatch=watch with {Active=false}};ActivityEventDirty=true;
                Audit("world","Experience",$"LiveDmObjectiveCompleted:{RegionId}:camp_watch:{watch.Occurrence};v{watch.RewardRevision};pool={watch.Experience};recipients={recipients}");
            }
            if(HasWorldNode)Audit("world","Experience",$"{reward.Activity}:{reward.SourceId};v{reward.Revision};pool={reward.Amount};recipients={recipients}");
        }
        _pveVictories.Clear();
    }

    private SavedProgression PrepareExperience(SavedProgression value,int amount,int recipients)
    {
        var catalog=_progressionCatalog!;
        if(value.Level==catalog.Progression.LevelCap)return value with {ExperienceFraction=null};
        var share=SavedExperienceFraction.Add(value.ExperienceFraction,amount,recipients);
        var award=ProgressionRules.Award(catalog.ExperienceCurve,catalog.Progression.StatPointsPerLevel,
            value.Level,value.Experience,checked((int)share.Whole));
        var next=value with {Level=award.Level,Experience=award.Experience,
            ExperienceFraction=award.Level==catalog.Progression.LevelCap?null:share.Remainder,
            StatPoints=checked(value.StatPoints+award.GrantedStatPoints),
            Allocation=award.GrantedStatPoints!=0 && value.Allocation is {} ledger?
                ledger with {EarnedFromLevels=checked(ledger.EarnedFromLevels+award.GrantedStatPoints)}:value.Allocation};
        if(next.StatPoints>100000)throw new InvalidOperationException("Available points exceed storage budget.");
        next.Allocation?.Validate(next.StatPoints);return next;
    }

    // Producer receipts (craft ordinal, completed quest, discovery bit, revealed cell) share this checkpoint.
    private void AwardPersonalExperience(NetworkEntityId id,ProgressionReward reward)
    {
        reward.Validate();
        if(!_progression.TryGetValue(id,out var value))return;
        _progression[id]=PrepareExperience(value,reward.Amount,1);DirtyProgression(id);
        if(HasWorldNode)Audit(Combat!.SourceIdentity(id).Id.ToString("N"),"Experience",
            $"{reward.Activity}:{reward.SourceId};v{reward.Revision};amount={reward.Amount}");
    }
}
