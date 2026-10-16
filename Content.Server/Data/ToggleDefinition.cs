using Content.Shared.Combat;

namespace Content.Server.Data;

/// <summary>Additional authored stops; death, final departure and crash always end sustaining.</summary>
public sealed record ToggleDefinition
{
    public required bool OccupiesAction {get;init;}
    public required ToggleStopTriggers StopOn {get;init;}
    internal void Validate(string path)
    {
        const ToggleStopTriggers known=ToggleStopTriggers.Movement|ToggleStopTriggers.Skill|ToggleStopTriggers.BasicAttack|ToggleStopTriggers.Defense|ToggleStopTriggers.EnemyInterrupt;
        if((StopOn&~known)!=0)throw new ArgumentException($"{path}: unknown toggle stop event.");
    }
}
