using System.Numerics;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Runtime data only; mutations belong to CombatSimulation.</summary>
public sealed class Combatant
{
    internal Combatant(NetworkEntityId id, CombatEntityKind kind, Vector2 position,
        DerivedStats stats, WeaponDefinition weapon, double interval,ControlBalance controlBalance,string definitionId)
    {
        Id = id; Kind = kind; Position = position; Stats = stats; Weapon = weapon;
        Health = stats.MaxHealth; AttackInterval = interval;
        BaseDerivedStats = stats;
        Resources = new(stats);
        DefinitionId=definitionId;
        Journal=kind==CombatEntityKind.Player?new():null;
        PublicJournalLabel=(kind switch{CombatEntityKind.Player=>"Игрок",CombatEntityKind.Monster=>"Монстр",CombatEntityKind.Boss=>"Босс",CombatEntityKind.Echo=>"Эхо",_=>"Манекен"})+" #"+id.Value;
        Controls=new(controlBalance);
    }

    public NetworkEntityId Id { get; }
    internal string DefinitionId {get;}
    internal CombatResources Resources {get;}
    internal CombatJournalState? Journal {get;}
    internal string PublicJournalLabel {get;}
    internal CombatSourceId SourceIdentity { get; set; }
    internal CombatSourceId? ContributionOwner {get;set;}
    internal double CombatUntil { get; set; }
    internal bool DetachedBody { get; set; }
    internal ulong DeathId;
    internal bool LifeDead;
    internal double CorpseEndsAt,RespawnReadyAt;
    public CombatEntityKind Kind { get; }
    public Vector2 Position { get; internal set; }
    public Vector2 Facing { get; internal set; } = Vector2.UnitY;
    public float Height { get; internal set; }
    public Vector3 Foot => new(Position.X, Height + AirOffset, Position.Y);
    public required HurtboxDefinition HurtboxProfile { get; init; }
    internal CombatCapsule Hurtbox => CombatCapsule.Create(HurtboxProfile, Foot, Facing, Control == CombatControlPhase.KnockedDown);
    public float AirOffset { get; internal set; }
    private CombatControlPhase _control;
    public CombatControlPhase Control
    {
        get=>_control;
        internal set
        {
            if((value==CombatControlPhase.KnockedDown)!=(_control==CombatControlPhase.KnockedDown))HurtboxChanged?.Invoke();
            _control=value;
        }
    }
    internal Action? HurtboxChanged {get;init;}
    internal double ControlUntil, FlightStarted, FlightSeconds, KnockdownSeconds;
    internal float FlightHeight;
    internal double RecoveryUntil, HitCancelAt;
    internal ControlSet Controls { get; }
    internal ControlResistance ControlResistance { get; init; } = new();
    internal ControlResistance[] EquipmentControlResistance {get;set;}=[];
    internal string[] HitFollowups = [];
    public bool Active { get; internal set; } = true;
    public double Health { get; internal set; }
    internal double HealthRecoveryElapsed { get; set; }
    public DerivedStats Stats { get; internal set; }
    internal DerivedStats BaseDerivedStats { get; set; }
    internal Dictionary<TimedEffectKey,TimedEffectInstance> Effects { get; } = new();
    internal HashSet<string> EnvironmentTags { get; set; } = new(StringComparer.Ordinal);
    internal bool PeriodicCriticalsAllowed { get; set; }
    internal bool ProfessionAllowsUnarmedBlock { get; set; }
    internal bool HasShield {get;set;}
    internal NetworkEntityId CompanionOwner {get;set;}
    internal CombatSourceId? CompanionFamily {get;set;}
    internal ushort ProfessionId {get;set;}
    internal DodgeProfileDefinition? BaseDodge {get;init;}
    internal DodgeProfileDefinition? ProfessionDodge {get;set;}
    internal LearnedPassive[] Passives {get;set;}=[];
    internal bool PassiveAllowsUnarmedBlock {get;set;}
    internal bool PassiveAllowsPeriodicCriticals {get;set;}
    public WeaponDefinition Weapon { get; internal set; }
    public double AttackInterval { get; internal set; }
    internal ActionInstance? Action { get; set; }
    public bool IsCasting => Action is {Finished:false,Phase:not Content.Shared.Combat.CombatActionPhase.Recovery};
    internal bool CanBleed { get; set; }
    internal bool CanBeStunned { get; set; }
    internal bool StationaryCast => Action is {Finished:false,MovementMultiplier:0};
    internal double StunnedUntil { get; set; }
    internal Vector2 LastFearOrigin;
    internal double FearRepathAt;
    internal bool FleeRouteActive;
    internal double SlowUntil { get; set; }
    internal float SlowFraction { get; set; }
    public uint LastAbilitySequence { get; internal set; }
    internal uint LastSequence { get; set; }
    internal uint LastNavigationOrder { get; set; }
    internal uint LastBasicControlSequence {get;set;}
    internal uint? LastBasicControlTick {get;set;}
    internal uint? LastRequestTick { get; set; }
    internal double ReadyAt { get; set; }
}
