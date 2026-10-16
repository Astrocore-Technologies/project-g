using Content.Server.Stats;
using Content.Shared.Combat;

namespace Content.Server.Data;

public enum ActionSpeedSource : byte { None, AttackSpeed, CastSpeed }

/// <summary>Movement, turning and cancellation are explicit for each phase, independently of hit geometry.</summary>
public sealed record ActionPhaseDefinition
{
    public double Seconds { get; init; }
    public ActionSpeedSource Speed { get; init; }
    public float MovementMultiplier { get; init; } = 1;
    public bool CanTurn { get; init; } = true;
    public ActionCancelPermission Cancel { get; init; }

    internal void Validate(string path)
    {
        const ActionCancelPermission known=ActionCancelPermission.Voluntary|ActionCancelPermission.Skill|ActionCancelPermission.Block|
            ActionCancelPermission.Parry|ActionCancelPermission.Dodge|ActionCancelPermission.BasicAttack|ActionCancelPermission.Movement;
        if(!double.IsFinite(Seconds) || Seconds<0 || !Enum.IsDefined(Speed) ||
            !float.IsFinite(MovementMultiplier) || MovementMultiplier<0 || (Cancel&~known)!=0)
            throw new ArgumentException($"{path}: invalid duration, speed, movement or cancellation window.");
    }

    internal double Duration(StatCalculator calculator,DerivedStats stats) => Speed switch
    {
        ActionSpeedSource.AttackSpeed=>calculator.AttackInterval(Seconds,stats),
        ActionSpeedSource.CastSpeed=>calculator.CastDuration(Seconds,stats),
        _=>Seconds
    };
}

/// <summary>Server-only action lifecycle. Payload, aim and hit geometry remain separate definitions.</summary>
public sealed record ActionDefinition
{
    public required CombatActionForm Form { get; init; }
    public required CombatActionCategory Category { get; init; }
    public ActionPhaseDefinition Preparation { get; init; } = new();
    public ActionPhaseDefinition Active { get; init; } = new();
    public ActionPhaseDefinition Recovery { get; init; } = new();
    public bool Interruptible { get; init; } = true;
    public ActionCapability Capabilities { get; init; }

    internal void Validate(string path)
    {
        if(!Enum.IsDefined(Form) || !Enum.IsDefined(Category) || Preparation is null || Active is null || Recovery is null)
            throw new ArgumentException($"{path}: missing or unknown action lifecycle.");
        if((Capabilities&~(ActionCapability.Magical|ActionCapability.Weapon|ActionCapability.Movement))!=0)
            throw new ArgumentException($"{path}: unknown action capabilities.");
        Preparation.Validate(path+".preparation"); Active.Validate(path+".active"); Recovery.Validate(path+".recovery");
        if(Form==CombatActionForm.Instant && Preparation.Seconds!=0)
            throw new ArgumentException($"{path}: an Instant action cannot have a cast preparation.");
    }

    internal ActionDurations Durations(StatCalculator calculator,DerivedStats stats) =>
        new(Preparation.Duration(calculator,stats),Active.Duration(calculator,stats),Recovery.Duration(calculator,stats));
}

internal readonly record struct ActionDurations(double Preparation,double Active,double Recovery);
