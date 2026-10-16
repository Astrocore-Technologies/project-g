using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Server.Configuration;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json.Nodes;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;
namespace Content.Tests.Server.Combat;

public sealed class CombatJournalTests
{
    private static ServerWorld WithEffect(string definition)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;data["effects"]!.AsArray().Add(JsonNode.Parse(definition));
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:Content.Server.Data.ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(_,_)=>true;return world;
    }
    [Fact]public void OrphanedDotKeepsUnknownSourceInBothTheFatalDamageAndDeathRecord()
    {
        var world=WithEffect("""{"id":"orphaned_dot","durationSeconds":2,"disposition":"Harmful","periodic":{"kind":"Damage","intervalSeconds":1,"power":{"flat":5}}}""");
        var source=world.AddPlayer(42,new(1));var target=world.AddPlayer(43,new(2));var combat=world.Combat!;
        Assert.True(combat.ApplyEffect(source.EntityId,target.EntityId,"orphaned_dot"));world.RemovePlayer(42);combat.Get(target.EntityId).Health=1;
        world.Simulate(1);var entries=Assert.Single(combat.DeathLogs(target.EntityId)).Entries;
        Assert.Equal(new[]{CombatJournalKind.PeriodicDamage,CombatJournalKind.Death},entries.Select(e=>e.Kind));
        Assert.All(entries,e=>Assert.Equal("Неизвестный источник",e.Source));Assert.Equal(1,entries[0].Amount);
    }
    [Fact]public void LethalReflectionHasItsOwnFatalCauseWithoutRewritingTheEarlierVictimReport()
    {
        var world=WithEffect("""{"id":"reflect","durationSeconds":5,"reflection":{"fraction":1,"direct":true}}""");
        var attacker=world.AddPlayer(42,new(1));var target=world.AddPlayer(43,new(2));var combat=world.Combat!;
        foreach(var id in new[]{attacker.EntityId,target.EntityId})
        {var actor=combat.Get(id);combat.ApplyEquipment(id,(actor.Stats with {PhysicalDefense=0},actor.Weapon,actor.AttackInterval));}
        combat.Get(attacker.EntityId).Health=1;combat.Get(target.EntityId).Health=4;combat.ApplyEffect(target.EntityId,target.EntityId,"reflect");
        combat.ApplyAbilityDamage(target.EntityId,10,attacker.EntityId);
        var first=Assert.Single(combat.DeathLogs(target.EntityId));Assert.DoesNotContain(first.Entries,e=>e.Kind==CombatJournalKind.ReflectedDamage);
        var reflected=Assert.Single(combat.DeathLogs(attacker.EntityId));Assert.Equal(CombatJournalKind.ReflectedDamage,reflected.Entries[^2].Kind);
        Assert.Equal(1,reflected.Entries[^2].Amount);Assert.Equal(reflected.Entries[^2].Source,reflected.Entries[^1].Source);
    }
    [Fact]public void CurrentLogHasTwoHundredEntriesAndDeathHasThreeHundredWithinThirtySeconds()
    {
        var journal=new CombatJournalState();
        for(var i=0;i<350;i++)journal.Record(100000+i,CombatJournalKind.Damage,"Монстр","Вы",1);
        Assert.Equal(200,journal.Current().Length);Assert.Equal(151UL,journal.Current()[0].Id);
        journal.Record(100350,CombatJournalKind.Death,"Монстр","Вы");journal.Died(1);
        var death=Assert.Single(journal.Deaths);Assert.Equal(300,death.Entries.Length);Assert.Equal(52UL,death.Entries[0].Id);
        Assert.Equal(CombatJournalKind.Death,death.Entries[^1].Kind);
        journal.Record(200000,CombatJournalKind.Damage,"Монстр","Вы",2);
        journal.Record(230000,CombatJournalKind.Death,"Монстр","Вы");journal.Died(2);
        Assert.Equal(2,journal.Deaths[^1].Entries.Length); // Inclusive 30-second boundary.
        journal.Record(260001,CombatJournalKind.Death,"Монстр","Вы");journal.Died(3);
        Assert.Single(journal.Deaths[^1].Entries);
    }
    [Fact]public void TenDeathsAreRetainedAndSessionTransferIsIndependentOfCallerArrays()
    {
        var journal=new CombatJournalState();
        for(ulong id=1;id<=12;id++){journal.Record((long)id*40000,CombatJournalKind.Death,"Монстр","Вы");journal.Died(id);}
        Assert.Equal(10,journal.Deaths.Count);Assert.Equal(3UL,journal.Deaths[0].DeathId);
        var logs=DeathLogDocuments.Deserialize(DeathLogDocuments.Serialize(journal.Deaths));var session=journal.Session();
        var next=new CombatJournalState();next.Restore(logs,session);logs[0].Entries[0]=default;session[0]=default;
        Assert.True(next.Deaths[0].Entries[0].IsValid);Assert.True(next.Session()[0].IsValid);
        var fresh=new CombatJournalState();fresh.Restore(next.Deaths);Assert.Empty(fresh.Current());Assert.Equal(10,fresh.Deaths.Count);
    }
    [Fact]public void FatalHitAndControlAreRecordedBeforeTheSameDeathAndOnlyActualHpDamageIsCounted()
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>0);
        var source=world.AddPlayer(42,new(1));var target=world.AddPlayer(43,new(2));
        var combat=world.Combat!;combat.DamagePermission=(_,_)=>true;combat.Get(target.EntityId).Health=20;
        var actor=combat.Get(source.EntityId);combat.ApplyEquipment(source.EntityId,(actor.Stats with {CriticalChance=1,CriticalDamage=2},actor.Weapon,actor.AttackInterval));
        Assert.True(combat.ApplyStatusControl(source.EntityId,target.EntityId,2,1,CrowdControlKind.Root,1));
        var hit=combat.ResolveDirectHit(source.EntityId,target.EntityId,System.Numerics.Vector2.UnitX,10000,true,skill:2);
        Assert.Equal(20,hit.Damage);Assert.True(hit.Critical);
        var death=Assert.Single(combat.DeathLogs(target.EntityId));Assert.Equal(1UL,death.DeathId);
        Assert.Equal(new[]{CombatJournalKind.Control,CombatJournalKind.Damage,CombatJournalKind.Death},death.Entries.Select(e=>e.Kind));
        var damage=death.Entries[1];Assert.Equal(20,damage.Amount);Assert.Equal(CombatJournalFlags.Critical|CombatJournalFlags.Magical,damage.Flags);
        Assert.Equal((ushort)2,damage.SkillId);Assert.Equal("Вы",damage.Target);Assert.Contains("Игрок",damage.Source);
        Assert.Equal(CombatJournalKind.Death,combat.CurrentJournal(source.EntityId)[^1].Kind);
    }
    [Fact]public void HealthPriceAndMultipleDeathsInOneTickKeepIndependentEvidence()
    {
        var world=CharacterPersistenceTests.World();var player=world.AddPlayer(42,new(1));var combat=world.Combat!;var id=player.EntityId;
        Assert.True(combat.PayHealth(id,combat.Get(id).Health,true));var first=Assert.Single(combat.DeathLogs(id));
        Assert.Equal(CombatJournalKind.HealthPrice,first.Entries[^2].Kind);
        Assert.True(combat.Resurrect(default,id,1,10));Assert.True(combat.PayHealth(id,10,true));
        Assert.Equal(2,combat.DeathLogs(id).Count);Assert.Equal(1UL,first.DeathId);Assert.Equal(2UL,combat.DeathLogs(id)[1].DeathId);
        Assert.Equal(2,first.Entries.Length);Assert.Contains(combat.DeathLogs(id)[1].Entries,e=>e.Kind==CombatJournalKind.Resurrection);
    }
    [Fact]public void UnrevealedSourceAndSkillStayUnknownToTheVictim()
    {
        var world=CharacterPersistenceTests.World();var source=world.AddPlayer(42,new(1));var target=world.AddPlayer(43,new(2));
        var combat=world.Combat!;combat.JournalVisibility=(_,_)=>false;combat.DamagePermission=(_,_)=>true;
        combat.ResolveDirectHit(source.EntityId,target.EntityId,System.Numerics.Vector2.UnitX,10,true,skill:17);
        var observed=Assert.Single(combat.CurrentJournal(target.EntityId));Assert.Equal("Неизвестный источник",observed.Source);Assert.Equal((ushort)0,observed.SkillId);
        var own=Assert.Single(combat.CurrentJournal(source.EntityId));Assert.Equal("Вы",own.Source);Assert.Equal((ushort)17,own.SkillId);
    }
    [Fact]public void InvalidDeathDocumentsFailClosedAndMaximumUnicodeEvidenceFitsItsBudget()
    {
        var entries=Enumerable.Range(1,300).Select(i=>new CombatJournalEntry((ulong)i,100000,i==300?CombatJournalKind.Death:CombatJournalKind.Damage,
            new string('界',64),new string('界',64),1)).ToArray();
        var log=new SavedDeathLog(1,1,100000,entries);var row=Assert.Single(DeathLogDocuments.Serialize([log]));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(row.State)<262144);Assert.Single(DeathLogDocuments.Deserialize([row]));
        Assert.Throws<InvalidDataException>(()=>DeathLogDocuments.Deserialize([row with {Version=2}]));
        Assert.Throws<InvalidDataException>(()=>DeathLogDocuments.Deserialize([row with {DeathId=2}]));
        Assert.Throws<InvalidDataException>(()=>(log with {Entries=[entries[1],entries[0],entries[^1]]}).Validate());
        Assert.Throws<InvalidDataException>(()=>(log with {AtUnixMilliseconds=130001}).Validate());
    }
}
