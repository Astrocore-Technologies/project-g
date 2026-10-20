using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private readonly Dictionary<NetworkEntityId,SavedDeathPenalty> _deathPenalties=new();

    private int ApplyDeathPenalty(NetworkEntityId id,bool pvp)
    {
        var life=Combat!.CaptureLife(id);
        if(!life.Dead || life.DeathId==0)throw new InvalidOperationException("Penalty requires an actual death.");
        if(_deathPenalties.TryGetValue(id,out var previous))
        {
            if(previous.DeathId>life.DeathId)throw new InvalidOperationException("Death ordinal moved backwards.");
            if(previous.DeathId==life.DeathId)return previous.ExperienceLost;
        }
        var progression=_progression[id];
        var lost=pvp ? (int)((long)progression.Experience*Rules.PvpExperiencePercent/100) :
            DeathPenaltyPolicy.PveLoss(_progressionCatalog!.ExperienceCurve,progression.Level,progression.Experience);
        _progression[id]=progression with {Experience=progression.Experience-lost};
        _deathPenalties[id]=new(1,life.DeathId,pvp,lost);_tavernDirty.Add(id);DirtyProgression(id);MarkPersistent(id);
        return lost;
    }
}
