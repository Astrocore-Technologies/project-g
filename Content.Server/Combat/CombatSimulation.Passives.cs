using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

internal readonly record struct LearnedPassive(AbilityDefinition Skill,int Level);

public sealed partial class CombatSimulation
{
    private void SetCreaturePassives(NetworkEntityId id,CreatureDefinition definition)
    {
        var actor=_actors[id];var learned=new List<LearnedPassive>();
        foreach(var skill in definition.AbilityIds)
            if(_catalog.Abilities[skill] is {Passive:not null} passive)learned.Add(new(passive,1));
        if(learned.Count==0)return;
        actor.Passives=learned.ToArray();RefreshEffectStats(id);actor.Health=actor.Stats.MaxHealth;
    }

    internal void SetLearnedPassives(NetworkEntityId id,SavedProgression progression)
    {
        var actor=_actors[id];var learned=new List<LearnedPassive>();
        foreach(var skill in progression.Skills)
            if(_catalog.Abilities[skill.DefinitionId] is {Passive:not null} definition)learned.Add(new(definition,skill.Level));
        var next=learned.ToArray();if(actor.Passives.AsSpan().SequenceEqual(next))return;
        actor.Passives=next;RefreshEffectStats(id);RefreshBlockPermission(id);
    }

    private bool PassiveAllows(Combatant actor,PassiveConditions conditions,WeaponDefinition? weapon=null)
    {
        if(conditions.Alive is {} alive && alive!=(actor.Health>0) || conditions.InCombat is {} combat && combat!=InCombat(actor.Id) ||
            conditions.WeaponIds.Length>0 && Array.IndexOf(conditions.WeaponIds,(weapon??actor.Weapon).Id)<0 ||
            conditions.ProfessionIds.Length>0 && Array.IndexOf(conditions.ProfessionIds,actor.ProfessionId)<0)return false;
        foreach(var tag in conditions.RequiredEnvironmentTags)if(!actor.EnvironmentTags.Contains(tag))return false;
        foreach(var tag in conditions.ExcludedEnvironmentTags)if(actor.EnvironmentTags.Contains(tag))return false;
        return true;
    }

    private DerivedStats ApplyPassive(DerivedStats stats,LearnedPassive learned)
    {
        var passive=learned.Skill.Passive!;
        return passive.Modifiers.IsEmpty?stats:passive.Modifiers.Apply(stats,
            passive.ScaleModifiersWithSkillLevel?_catalog.SkillProgressions[learned.Skill.Id].PowerFactor(learned.Level):1,clampMaxHealth:false);
    }

    private DerivedStats WithPassives(Combatant actor,DerivedStats stats,WeaponDefinition? weapon=null)
    {
        foreach(var learned in actor.Passives)
            if(PassiveAllows(actor,learned.Skill.Passive!.Conditions,weapon))stats=ApplyPassive(stats,learned);
        return stats;
    }

    private void RefreshPassivePermissions(Combatant actor)
    {
        actor.PassiveAllowsUnarmedBlock=actor.PassiveAllowsPeriodicCriticals=false;
        foreach(var learned in actor.Passives)
        {
            var passive=learned.Skill.Passive!;if(!PassiveAllows(actor,passive.Conditions))continue;
            actor.PassiveAllowsUnarmedBlock|=passive.AllowsUnarmedBlock;
            actor.PassiveAllowsPeriodicCriticals|=passive.AllowsPeriodicCriticals;
        }
    }

    private void RefreshPassiveConditions(NetworkEntityId id)
    {
        if(!_actors.TryGetValue(id,out var actor) || actor.Passives.Length==0)return;
        RefreshEffectStats(id);RefreshBlockPermission(id);
    }

    internal string[]? CapturePassiveModifiers(NetworkEntityId id)
    {
        var actor=_actors[id];if(actor.Passives.Length==0)return null;
        var active=new List<string>();
        foreach(var learned in actor.Passives)
            if(PassiveAllows(actor,learned.Skill.Passive!.Conditions))active.Add(learned.Skill.Id);
        return active.ToArray();
    }

    private DerivedStats PassiveStatsAtSave(Combatant actor,DerivedStats stats,string[]? active)
    {
        if(active is null)return WithPassives(actor,stats);
        foreach(var id in active)
        {
            var found=false;
            foreach(var learned in actor.Passives)
                if(learned.Skill.Id==id){stats=ApplyPassive(stats,learned);found=true;break;}
            if(!found)throw new InvalidDataException("Saved passive modifier has no learned passive source.");
        }
        return stats;
    }
}
