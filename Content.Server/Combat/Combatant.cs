using System.Numerics;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Runtime data only; mutations belong to CombatSimulation.</summary>
public sealed class Combatant
{
    internal Combatant(NetworkEntityId id, CombatEntityKind kind, Vector2 position,
        DerivedStats stats, WeaponDefinition weapon, double interval,ControlBalance controlBalance)
    {
        Id = id; Kind = kind; Position = position; Stats = stats; Weapon = weapon;
        Health = stats.MaxHealth; AttackInterval = interval;
        BaseDerivedStats = stats;
        Journal=kind==CombatEntityKind.Player?new():null;
        PublicJournalLabel=(kind switch{CombatEntityKind.Player=>"Игрок",CombatEntityKind.Monster=>"Монстр",CombatEntityKind.Boss=>"Босс",_=>"Манекен"})+" #"+id.Value;
        Controls=new(controlBalance);
    }

    public NetworkEntityId Id { get; }
    internal CombatJournalState? Journal {get;}
    internal string PublicJournalLabel {get;}
    internal CombatSourceId SourceIdentity { get; set; }
    internal double CombatUntil { get; set; }
    internal bool DetachedBody { get; set; }
    internal ulong DeathId;
    internal bool LifeDead;
    internal double CorpseEndsAt,RespawnReadyAt;
    public CombatEntityKind Kind { get; }
    public Vector2 Position { get; internal set; }
    public float Height { get; internal set; }
    public Vector3 Foot => new(Position.X, Height + AirOffset, Position.Y);
    public float AirOffset { get; internal set; }
    public CombatControlPhase Control { get; internal set; }
    internal double ControlUntil, FlightStarted, FlightSeconds, KnockdownSeconds;
    internal float FlightHeight;
    internal double RecoveryUntil, HitCancelAt;
    internal ControlSet Controls { get; }
    internal ControlResistance ControlResistance { get; init; } = new();
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
    internal ushort ProfessionId {get;set;}
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
    internal uint? LastRequestTick { get; set; }
    internal double ReadyAt { get; set; }
}
