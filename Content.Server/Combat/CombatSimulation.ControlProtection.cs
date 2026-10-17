using Content.Server.Data;
using Content.Server.Stats;
using Content.Shared.Combat;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private (double Factor,bool Immune) CurrentControlProtection(Combatant actor,CrowdControlKind kind)
    {
        var type=0d;var category=0d;var immune=false;
        AddProtection(actor.ControlResistance,kind,1,ref type,ref category,ref immune);
        foreach(var equipment in actor.EquipmentControlResistance)AddProtection(equipment,kind,1,ref type,ref category,ref immune);
        foreach(var learned in actor.Passives)
            if(PassiveAllows(actor,learned.Skill.Passive!.Conditions))
                AddProtection(learned.Skill.Passive.ControlResistance,kind,1,ref type,ref category,ref immune);
        foreach(var effect in actor.Effects.Values)
            if(effect.EndsAt>=EffectTime&&EnvironmentAllows(actor,effect.Definition))
                AddProtection(effect.Definition.ControlResistance,kind,effect.Stacks,ref type,ref category,ref immune);
        return(immune?0:(1-Math.Clamp(type,0,1))*(1-Math.Clamp(category,0,1)),immune);
    }
    private static void AddProtection(ControlResistance protection,CrowdControlKind kind,int stacks,
        ref double type,ref double category,ref bool immune)
    {
        immune|=Array.IndexOf(protection.Immunities,kind)>=0;
        type=StatMath.Add(type,StatMath.Multiply(protection.Types.GetValueOrDefault(kind),stacks));
        category=StatMath.Add(category,StatMath.Multiply(protection.Categories.GetValueOrDefault(AdaptiveControlLedger.Category(kind)),stacks));
    }
}
