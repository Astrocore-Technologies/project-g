using System.Numerics;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>One NPC life/encounter. Runtime threat candidates and persistent participation credit stay separate.</summary>
internal sealed class NpcEncounter(ThreatDefinition definition)
{
    internal ThreatDefinition Definition {get;}=definition;
    internal Dictionary<NetworkEntityId,double> Threat {get;}=new();
    internal ContributionLedger Contribution {get;}=new();
    internal List<NetworkEntityId> Invalid {get;}=new();
    internal Vector2 Home {get;set;}
    internal float? LeashRadius {get;set;}
    internal bool Closed {get;set;}
    internal ulong ExperienceDeathId {get;set;}
    internal bool Returning {get;set;}
    internal void AddThreat(NetworkEntityId source,double amount)
        =>Threat[source]=StatMath.Add(Threat.GetValueOrDefault(source),amount);
}
