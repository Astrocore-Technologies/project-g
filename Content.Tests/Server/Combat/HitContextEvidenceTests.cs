using System.Numerics;
using System.Text.Json.Nodes;
using Content.Server.Persistence;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class HitContextEvidenceTests
{
    [Fact]public void ACommittedDeathDocumentPreservesContextAndLegacyEvidenceLoadsWithoutInventingIt()
    {
        var damage=new CombatJournalEntry(1,100000,CombatJournalKind.Damage,"Источник","Вы",1,HitTags:CombatHitTags.BackHit|CombatHitTags.AirborneHit);
        var death=new CombatJournalEntry(2,100000,CombatJournalKind.Death,"Источник","Вы");
        var log=new SavedDeathLog(1,1,100000,[damage,death]);var row=Assert.Single(DeathLogDocuments.Serialize([log]));
        Assert.Equal(log.Entries,Assert.Single(DeathLogDocuments.Deserialize([row])).Entries);
        var legacy=JsonNode.Parse(row.State)!;foreach(var entry in legacy["Entries"]!.AsArray())entry!.AsObject().Remove("HitTags");
        var restored=Assert.Single(DeathLogDocuments.Deserialize([row with {State=legacy.ToJsonString()}]));
        Assert.All(restored.Entries,e=>Assert.Equal(CombatHitTags.None,e.HitTags));
        var session=DeathLogDocuments.SerializeSession([damage]);Assert.Equal(damage,Assert.Single(DeathLogDocuments.DeserializeSession(session)));
        var oldSession=JsonNode.Parse(session)!.AsArray();oldSession[0]!.AsObject().Remove("HitTags");
        Assert.Equal(CombatHitTags.None,Assert.Single(DeathLogDocuments.DeserializeSession(oldSession.ToJsonString())).HitTags);
        legacy["Entries"]![0]!["HitTags"]=128;
        Assert.Throws<InvalidDataException>(()=>DeathLogDocuments.Deserialize([row with {State=legacy.ToJsonString()}]));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ContextEvidenceSurvivesNumericEvasionAndFullyAbsorbedContacts(bool evaded)
    {
        var combat=new CombatSimulation(ContentCatalogTests.Load(),new SpatialIndex(8),
            new NavigationOptions().CreateGrid(new MovementOptions().ToSettings()),new CombatOptions(),20,8,()=>1,()=>evaded?1:0);
        var player=new NetworkEntityId(1);var source=new NetworkEntityId(2);
        combat.Add(player,Vector2.Zero,CombatEntityKind.Player);combat.Add(source,Vector2.UnitY,CombatEntityKind.Monster);
        var result=combat.ResolveDirectHit(source,player,-Vector2.UnitY,0,false,projectile:true);
        Assert.Equal(CombatHitTags.FrontHit,result.Tags);Assert.Equal(evaded,result.Evaded);
        var entry=Assert.Single(combat.CurrentJournal(player));
        Assert.Equal(evaded?CombatJournalKind.Evasion:CombatJournalKind.Absorbed,entry.Kind);
        Assert.Equal(result.Tags,entry.HitTags);Assert.Equal(0,entry.Amount);
    }
}
