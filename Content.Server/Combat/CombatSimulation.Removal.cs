using Content.Server.Data;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal int ResolveRemoval(NetworkEntityId source,NetworkEntityId target,SkillRemovalDefinition removal,bool cleanse,ushort skill,
        bool allowOtherPlayers=false)
    {
        if(!_actors.TryGetValue(source,out var author) || author.Health<=0 || !author.Active ||
            !_actors.TryGetValue(target,out var actor) || actor.Health<=0 || !actor.Active ||
            (cleanse ? !CanSupport(source,target,allowOtherPlayers) : !CanTarget(source,target))) return 0;
        var removed=0;
        if(removal.Timed)
        {
            // The filter is authored once, not allocated for every target of an area.
            removed=RemoveEffects(target,cleanse,removal.Tags);
            if(removed>0) RecordJournal(source,target,cleanse ? CombatJournalKind.EffectsCleansed : CombatJournalKind.EffectsDispelled,removed,skill:skill);
        }
        if(cleanse)
            foreach(var kind in removal.Controls)
            {
                var count=0;
                foreach(var control in actor.Controls.Active) if(control.Key.Kind==kind && control.EndsAt>_time) count++;
                if(count==0 || !RemoveControl(target,kind)) continue;
                removed+=count;RecordJournal(source,target,CombatJournalKind.ControlRemoved,count,skill:skill,control:kind);
            }
        return removed;
    }
}
