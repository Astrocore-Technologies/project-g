using Content.Shared.Combat;

namespace Content.Server.Data;

public sealed record EchoSummonDefinition
{
    public required double ManaCost {get;init;}
    public required ActionDefinition Action {get;init;}

    internal void Validate()
    {
        if(Action is null)throw new ArgumentException("Missing Echo summon action.");
        Action.Validate("echoSummon.action");
        if(!double.IsFinite(ManaCost) || ManaCost<0 || Action.Form!=CombatActionForm.Cast ||
            Action.Category!=CombatActionCategory.Companion || Action.Capabilities!=ActionCapability.Magical ||
            Action.Preparation.Seconds<=0 || Action.Preparation.Speed!=ActionSpeedSource.None ||
            Action.Preparation.MovementMultiplier!=0 || !Action.Preparation.CanTurn ||
            Action.Active.Seconds!=0 || Action.Recovery.Seconds!=0 || !Action.Interruptible)
            throw new ArgumentException("Invalid Echo summon payment or fixed magical Cast lifecycle.");
    }
}
