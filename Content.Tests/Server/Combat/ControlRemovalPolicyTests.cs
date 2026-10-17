using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ControlRemovalPolicyTests
{
    private static JsonNode Document()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![1]!;
        skill["power"]=1;skill["magicAttackScale"]=0;skill["radius"]=.5;
        skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
        skill["effects"]=JsonNode.Parse("""[{"operation":"Control","trigger":"Contact","target":"HitTarget","control":{"kind":"Root","durationSeconds":2,"removal":{"cleansable":false,"tags":["curse"]}}}]""");
        return data;
    }
    private static ContentCatalog Parse(JsonNode data)=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    private sealed class Fight
    {
        internal readonly ServerWorld World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Parse(Document()),hitRoll:()=>0,criticalRoll:()=>1);
        internal CombatSimulation Combat=>World.Combat!;
        internal readonly ServerPlayer Source,Target;
        internal Fight()
        {
            Source=World.AddPlayer(1,new(1),World.CreateInitialCharacter() with {X=-7,Z=0});
            Target=World.AddPlayer(2,new(2),World.CreateInitialCharacter() with {X=-6,Z=0});
            Combat.DamagePermission=(_,_)=>true;
            Assert.True(World.TryQueueAbility(1,new(1,World.Tick,2,Target.Position),0));World.Simulate(.05f);
            Assert.False(Assert.Single(Combat.Get(Target.EntityId).Controls.Active).Removal.Cleansable);
        }
        internal int Cleanse(string[]? tags=null)=>Combat.ResolveRemoval(Source.EntityId,Target.EntityId,
            new(){Timed=false,Controls=[CrowdControlKind.Root],ControlTags=tags},true,99,true);
    }

    [Fact]public void CleanseFiltersEachInstanceAndKeepsLockedControlsAndSpentBudget()
    {
        var f=new Fight();var actor=f.Combat.Get(f.Target.EntityId);
        Assert.True(f.Combat.ApplyStatusControl(f.Source.EntityId,f.Target.EntityId,10,10,CrowdControlKind.Root,1,
            removal:new(){Tags=["poison"]}));
        Assert.True(f.Combat.ApplyStatusControl(f.Source.EntityId,f.Target.EntityId,11,11,CrowdControlKind.Root,1,
            removal:new(){Tags=["curse"]}));
        var budget=actor.Controls.Ledger.Snapshot(ControlCategory.Movement,f.Combat.Time).Used;
        Assert.Equal(1,f.Cleanse(["poison"]));Assert.Equal(2,actor.Controls.Active.Count());
        Assert.Equal(2u,f.Combat.ControlFeedbackEvents[^1].RemainingInstances);
        Assert.Equal(0,f.Cleanse(["poison"]));Assert.Equal(1,f.Cleanse());
        var locked=Assert.Single(actor.Controls.Active);Assert.False(locked.Removal.Cleansable);
        Assert.Equal(0,f.Cleanse());Assert.Equal(budget,actor.Controls.Ledger.Snapshot(ControlCategory.Movement,f.Combat.Time).Used);
        Assert.True(f.Combat.RemoveControl(f.Target.EntityId,CrowdControlKind.Root)); // Explicit non-Cleanse escape/removal remains separate.
        Assert.False(actor.Controls.HasActive);
    }

    [Fact]public void RemovalPolicyAndTagsSurviveDocumentRoundTripAndCharacterRestore()
    {
        var f=new Fight();var before=Assert.Single(f.Combat.CaptureEffects(f.Target.EntityId).OfType<SavedControlEffect>());
        var rows=CombatEffectDocuments.Serialize([before]);var restored=Assert.IsType<SavedControlEffect>(Assert.Single(CombatEffectDocuments.Deserialize(rows)));
        Assert.False(restored.Removal.Cleansable);Assert.Equal(new[]{"curse"},restored.Removal.Tags);
        var saved=f.World.CaptureCharacter(2) with {Effects=[restored]};f.World.RemovePlayer(2);
        var player=f.World.AddPlayer(2,new(22),saved);var active=Assert.Single(f.Combat.Get(player.EntityId).Controls.Active);
        Assert.Equal(before.InstanceId,active.InstanceId);Assert.False(active.Removal.Cleansable);Assert.Equal(new[]{"curse"},active.Removal.Tags);
        Assert.Equal(0,f.Combat.ResolveRemoval(f.Source.EntityId,player.EntityId,new(){Timed=false,Controls=[CrowdControlKind.Root]},true,99,true));
    }

    [Fact]public void ExistingVersionOneDocumentsWithoutMetadataKeepTheirPreviousCleansePermission()
    {
        var f=new Fight();var row=Assert.Single(CombatEffectDocuments.Serialize(f.Combat.CaptureEffects(f.Target.EntityId)));
        var legacy=JsonNode.Parse(row.State)!.AsObject();Assert.True(legacy.Remove("Removal"));
        var restored=Assert.IsType<SavedControlEffect>(Assert.Single(CombatEffectDocuments.Deserialize([new(row.InstanceId,row.Version,legacy.ToJsonString())])));
        Assert.True(restored.Removal.Cleansable);Assert.Empty(restored.Removal.Tags);Assert.Equal(1,restored.Version);
    }

    [Fact]public void UncleanseableLaunchKeepsItsMotionButQuickRecoverUsesItsOwnPermission()
    {
        var f=new Fight();Assert.True(f.Combat.RemoveControl(f.Target.EntityId,CrowdControlKind.Root));
        Assert.True(f.Combat.Knockup(f.Target.EntityId,.2,1,1,f.Source.EntityId,10,10,removal:new(){Cleansable=false}));
        var removal=new SkillRemovalDefinition{Timed=false,Controls=[CrowdControlKind.Knockup]};
        Assert.Equal(0,f.Combat.ResolveRemoval(f.Source.EntityId,f.Target.EntityId,removal,true,99,true));
        f.World.Simulate(.2f);Assert.Equal(CombatControlPhase.KnockedDown,f.Combat.Get(f.Target.EntityId).Control);
        Assert.Equal(DefenseOutcome.Accepted,f.Combat.QuickRecover(f.Target.EntityId,new(1,DefenseAction.QuickRecover,System.Numerics.Vector2.UnitY)));
        Assert.False(f.Combat.Get(f.Target.EntityId).Controls.Has(CrowdControlKind.Knockup));
    }

    [Theory][InlineData("null")][InlineData("duplicate")][InlineData("syntax")][InlineData("oversized")]
    public void InvalidControlMetadataFailsContentAndSavedDocumentValidation(string invalid)
    {
        var data=Document();var policy=data["abilities"]![1]!["effects"]![0]!["control"]!["removal"]!;
        policy["tags"]=invalid switch
        {
            "null"=>null,"duplicate"=>new JsonArray("curse","curse"),"syntax"=>new JsonArray("private secret"),
            _=>new JsonArray(Enumerable.Range(0,33).Select(i=>(JsonNode?)JsonValue.Create("tag"+i)).ToArray())
        };
        Assert.Throws<InvalidDataException>(()=>Parse(data));
        var f=new Fight();var saved=Assert.IsType<SavedControlEffect>(Assert.Single(f.Combat.CaptureEffects(f.Target.EntityId)));
        var tags=invalid switch {"null"=>null!,"duplicate"=>new[]{"curse","curse"},"syntax"=>["private secret"],_=>Enumerable.Range(0,33).Select(i=>"tag"+i).ToArray()};
        Assert.Throws<InvalidDataException>(()=>(saved with {Removal=new(){Tags=tags}}).Validate());
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ControlTagSelectorRequiresExplicitKindsAndCleanse(bool dispel)
    {
        var data=AuthoredRemovalTests.Document(dispel?"Dispel":"Cleanse");
        data["abilities"]![1]!["effects"]![0]!["removal"]!["controlTags"]=new JsonArray("curse");
        Assert.Throws<InvalidDataException>(()=>Parse(data));
    }
}
