using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class AuthoredRemovalTests
{
    internal static JsonNode Document(string operation="Cleanse",params CrowdControlKind[] kinds)
    {
        var data=SupportSkillTests.Document(allowOthers:true);var skill=data["abilities"]![1]!;var application=skill["effects"]![0]!;
        application["operation"]=operation;application.AsObject().Remove("power");
        application["removal"]=new JsonObject
        { ["timed"]=true,["controls"]=new JsonArray(kinds.Select(k=>(JsonNode?)JsonValue.Create(k.ToString())).ToArray()),["tags"]=new JsonArray("poison") };
        if(operation=="Dispel")
        {
            skill.AsObject().Remove("contactDisposition");application["allowOtherPlayers"]=false;
            application["removal"]!["tags"]=new JsonArray("blessing");
        }
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"poison","durationSeconds":5,"disposition":"Harmful","cleansable":true,"tags":["poison"],"modifiers":{"meleeAttack":-5}}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"curse","durationSeconds":5,"disposition":"Harmful","cleansable":true,"tags":["curse"]}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"locked_poison","durationSeconds":5,"disposition":"Harmful","tags":["poison"]}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"blessing","durationSeconds":5,"dispellable":true,"tags":["blessing"],"modifiers":{"magicDefense":5}}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"locked_blessing","durationSeconds":5,"tags":["blessing"]}"""));
        return data;
    }
    private static ContentCatalog Parse(JsonNode data)=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    private static (ServerWorld World,ServerPlayer Source,ServerPlayer Target) Setup(JsonNode data)
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Parse(data),hitRoll:()=>0,criticalRoll:()=>1);
        var source=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {X=-7,Z=0});
        var target=world.AddPlayer(43,new(2),world.CreateInitialCharacter() with {X=-6,Z=0});
        world.Combat!.DamagePermission=(_,_)=>true;return(world,source,target);
    }
    private static void Cast(ServerWorld world,ServerPlayer source,Vector2 aim)
    {
        Assert.True(world.TryQueueAbility(source.ConnectionId,new(1,world.Tick,2,aim),0));
        for(var i=0;i<9;i++)world.Simulate(.05f);
    }

    [Fact]public void CleanseSelectsOnlyTaggedEligibleEffectsAndExplicitControlKindsWithoutRefundingAcr()
    {
        var (world,source,target)=Setup(Document("Cleanse",CrowdControlKind.Freeze));var combat=world.Combat!;
        foreach(var effect in new[]{"poison","curse","locked_poison","blessing"})Assert.True(combat.ApplyEffect(default,target.EntityId,effect));
        combat.ApplyStatusControl(default,target.EntityId,1,1,CrowdControlKind.Freeze,2);
        combat.ApplyStatusControl(default,target.EntityId,2,2,CrowdControlKind.Root,2);var actor=combat.Get(target.EntityId);
        var movement=actor.Controls.Ledger.Snapshot(ControlCategory.Movement,combat.Time).Used;
        var hard=actor.Controls.Ledger.Snapshot(ControlCategory.Hard,combat.Time).Used;
        Cast(world,source,target.Position);
        Assert.Equal(new[]{"blessing","curse","locked_poison"},actor.Effects.Values.Select(e=>e.Definition.Id).Order().ToArray());
        Assert.False(actor.Controls.Has(CrowdControlKind.Freeze));Assert.True(actor.Controls.Has(CrowdControlKind.Root));Assert.False(combat.IsStunned(target.EntityId));
        Assert.Equal(movement,actor.Controls.Ledger.Snapshot(ControlCategory.Movement,combat.Time).Used);
        Assert.Equal(hard,actor.Controls.Ledger.Snapshot(ControlCategory.Hard,combat.Time).Used);
        Assert.Contains(combat.CurrentJournal(target.EntityId),e=>e.Kind==CombatJournalKind.ControlRemoved && e.Control==CrowdControlKind.Freeze && e.Amount==1);
        Assert.Contains(combat.CurrentJournal(target.EntityId),e=>e.Kind==CombatJournalKind.EffectsCleansed && e.Amount==1);
        Assert.Empty(world.Abilities!.Hits);Assert.Equal(92,world.Abilities.Mana(source.EntityId));
        var saved=world.CaptureCharacter(target.ConnectionId);world.RemovePlayer(target.ConnectionId);
        var restored=world.AddPlayer(target.ConnectionId,new(2),saved);var reopened=combat.Get(restored.EntityId);
        Assert.False(reopened.Controls.Has(CrowdControlKind.Freeze));Assert.True(reopened.Controls.Has(CrowdControlKind.Root));
        Assert.DoesNotContain(reopened.Effects.Values,e=>e.Definition.Id=="poison");
        Assert.Equal(hard,reopened.Controls.Ledger.Snapshot(ControlCategory.Hard,combat.Time).Used);
    }

    [Fact]public void DispelRemovesOnlyPositiveDispellableEffectsAndNeverCleansesTheVictim()
    {
        var (world,source,target)=Setup(Document("Dispel"));var combat=world.Combat!;var actor=combat.Get(target.EntityId);
        foreach(var effect in new[]{"poison","blessing","locked_blessing"})Assert.True(combat.ApplyEffect(default,target.EntityId,effect));
        var defense=actor.Stats.MagicDefense;Cast(world,source,target.Position);
        Assert.Equal(new[]{"locked_blessing","poison"},actor.Effects.Values.Select(e=>e.Definition.Id).Order().ToArray());
        Assert.Equal(defense-5,actor.Stats.MagicDefense);
        Assert.Contains(combat.CurrentJournal(target.EntityId),e=>e.Kind==CombatJournalKind.EffectsDispelled && e.Amount==1);
        Assert.DoesNotContain(combat.CurrentJournal(target.EntityId),e=>e.Kind==CombatJournalKind.EffectsCleansed);
    }

    [Fact]public void SupportMembershipIsRecheckedAtImpactBeforeAnyCleanse()
    {
        var data=Document();data["abilities"]![1]!["effects"]![0]!["allowOtherPlayers"]=false;
        var (world,source,target)=Setup(data);var member=true;world.Combat!.SupportPermission=(_,_)=>member;
        Assert.True(world.Combat.ApplyEffect(default,target.EntityId,"poison"));
        Assert.True(world.TryQueueAbility(source.ConnectionId,new(1,world.Tick,2,target.Position),0));world.Simulate(.05f);member=false;
        for(var i=0;i<8;i++)world.Simulate(.05f);
        Assert.Contains(world.Combat.Get(target.EntityId).Effects.Values,e=>e.Definition.Id=="poison");
        Assert.DoesNotContain(world.Combat.CurrentJournal(target.EntityId),e=>e.Kind==CombatJournalKind.EffectsCleansed);
    }

    [Fact]public void RemovalOfTheLastDisplacementStopsItsRealMotionAndKeepsSpentBudget()
    {
        var (world,source,target)=Setup(Document("Cleanse",CrowdControlKind.Knockback));var combat=world.Combat!;
        Assert.True(combat.Knockback(target.EntityId,Vector2.UnitX,3,1));world.Simulate(.05f);var stopped=target.Position;
        var removal=new SkillRemovalDefinition{Timed=false,Controls=[CrowdControlKind.Knockback]};
        Assert.Equal(1,combat.ResolveRemoval(source.EntityId,target.EntityId,removal,true,2,true));
        Assert.Equal(CombatControlPhase.None,combat.Get(target.EntityId).Control);Assert.False(target.Motion.IsMoving);
        for(var i=0;i<6;i++)world.Simulate(.05f);Assert.Equal(stopped,target.Position);
        Assert.Equal(3,combat.Get(target.EntityId).Controls.Ledger.Snapshot(ControlCategory.Movement,combat.Time).Used,6);
    }

    [Fact]public void RemovingKnockupEndsItsFlightButPreservesAnIndependentKnockdown()
    {
        var (world,source,target)=Setup(Document());var combat=world.Combat!;
        Assert.True(combat.Knockup(target.EntityId,1,1,1));world.Simulate(.1f);Assert.True(combat.Get(target.EntityId).AirOffset>0);
        Assert.True(combat.ApplyStatusControl(default,target.EntityId,7,7,CrowdControlKind.Knockdown,1));
        Assert.Equal(1,combat.ResolveRemoval(source.EntityId,target.EntityId,new(){Timed=false,Controls=[CrowdControlKind.Knockup]},true,2,true));
        Assert.Equal(0,combat.Get(target.EntityId).AirOffset);Assert.Equal(CombatControlPhase.KnockedDown,combat.Get(target.EntityId).Control);
        Assert.True(combat.Get(target.EntityId).Controls.Has(CrowdControlKind.Knockdown));
        Assert.Equal(1,combat.ResolveRemoval(source.EntityId,target.EntityId,new(){Timed=false,Controls=[CrowdControlKind.Knockdown]},true,2,true));
        Assert.Equal(CombatControlPhase.None,combat.Get(target.EntityId).Control);
        Assert.True(combat.Get(target.EntityId).Controls.Ledger.Snapshot(ControlCategory.Hard,combat.Time).Used>2);
    }

    [Theory][InlineData("missingRemoval")][InlineData("unexpectedPower")][InlineData("emptySelection")]
    [InlineData("missingControls")][InlineData("unknownControl")][InlineData("duplicateControl")][InlineData("emptyTags")]
    [InlineData("tagsWithoutTimed")][InlineData("dispelControls")][InlineData("hostileCleanseTarget")]
    [InlineData("supportDispel")][InlineData("unexpectedRemoval")]
    public void InvalidRemovalSelectionFailsAtContentLoad(string scenario)
    {
        var data=Document();var skill=data["abilities"]![1]!;var payload=skill["effects"]![0]!;var removal=payload["removal"]!;
        switch(scenario)
        {
            case "missingRemoval":payload.AsObject().Remove("removal");break;
            case "unexpectedPower":payload["power"]=new JsonObject{["flat"]=1};break;
            case "emptySelection":removal["timed"]=false;removal.AsObject().Remove("tags");break;
            case "missingControls":removal.AsObject().Remove("controls");break;
            case "unknownControl":removal["controls"]=new JsonArray("Unknown");break;
            case "duplicateControl":removal["controls"]=new JsonArray("Root","Root");break;
            case "emptyTags":removal["tags"]=new JsonArray();break;
            case "tagsWithoutTimed":removal["timed"]=false;removal["controls"]=new JsonArray("Root");break;
            case "dispelControls":payload["operation"]="Dispel";payload["allowOtherPlayers"]=false;skill.AsObject().Remove("contactDisposition");removal["controls"]=new JsonArray("Root");break;
            case "hostileCleanseTarget":skill.AsObject().Remove("contactDisposition");break;
            case "supportDispel":payload["operation"]="Dispel";break;
            case "unexpectedRemoval":payload["operation"]="Heal";payload["power"]=new JsonObject{["flat"]=1};break;
        }
        Assert.Throws<InvalidDataException>(()=>Parse(data));
    }
}
