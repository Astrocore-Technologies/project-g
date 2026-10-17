using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

internal readonly record struct ControlFeedbackEvent(ulong Id,CombatSourceId Source,NetworkEntityId Target,
    CrowdControlKind Control,ControlChangeKind Change,double Duration,double Strength,uint RemainingInstances,ControlMitigation Mitigation);

public sealed partial class CombatSimulation
{
    private readonly List<ControlFeedbackEvent> _controlFeedback=new();
    private ulong _controlFeedbackSerial;
    internal IReadOnlyList<ControlFeedbackEvent> ControlFeedbackEvents=>_controlFeedback;

    private void ObserveControls(NetworkEntityId target)
        =>_actors[target].Controls.Changed=change=>
        {
            RecordEncounterControl(target,change);
            if(_controlFeedbackSerial==ulong.MaxValue)throw new InvalidOperationException("Control feedback identity exhausted.");
            _controlFeedback.Add(new(++_controlFeedbackSerial,change.Instance.Key.Source,target,change.Instance.Key.Kind,change.Kind,
                change.Duration,change.Kind is ControlChangeKind.Applied or ControlChangeKind.Refreshed ? change.Instance.Strength : 0,
                change.RemainingInstances,change.Mitigation));
        };

    private bool ControlResistanceAllows(Combatant actor,NetworkEntityId source,ushort definition,ref ulong execution,
        CrowdControlKind kind,ReleasedSourceSnapshot? releasedSource,out double factor,bool nativeImmunity=false)
    {
        if(execution==0)execution=AllocateExecution();
        var protection=CurrentControlProtection(actor,kind);
        factor=protection.Factor;
        if(!nativeImmunity && factor>0)return true;
        var immunity=nativeImmunity || protection.Immune;
        actor.Controls.ReportRejected(new(releasedSource?.Identity ?? SourceIdentity(source),definition,kind),execution,
            immunity ? ControlMitigation.Immunity : ControlMitigation.Resistance,_time);
        return false;
    }
}
