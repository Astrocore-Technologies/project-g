using System.Numerics;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Immutable release context, owned by the bounded projectile rather than an actor/session.</summary>
internal sealed record ReleasedSourceSnapshot(NetworkEntityId Actor,CombatSourceId Identity,CombatEntityKind Kind,
    Vector2 Position,DerivedStats Stats,double WeaponPower,bool PeriodicCriticalsAllowed,double SkillPower,
    ReleasedPvpSource? Pvp);

internal sealed record ReleasedPvpSource(Guid Character,ulong Party,SavedPvp State);
