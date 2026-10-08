using System.Numerics;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class ProgressionTests
{
    [Fact]
    public void AreaPracticesOnceForMultipleTargetsAndDeathCancelsDashPractice()
    {
        var w = new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),
            npc:Options.Create(new NpcOptions { Enabled = true,X = -7,Z = 3,AggroRadius = 1 }));
        var p = w.AddPlayer(42,new(1),w.CreateInitialCharacter() with { Health = 1 }); var hits = 0;
        w.TryQueueAbility(42,new(1,0,2,new(-7,3)),0);
        for (var i=0;i<40;i++) { Step(w); hits += w.Abilities!.Hits.Count; }
        Assert.Equal(2,hits); Assert.Equal(1,Skill(w,p,2).Practice); Assert.Equal(2,w.ProgressionState(p.EntityId,w.Tick).Experience);
        w.TryQueueAbility(42,new(2,0,3,Vector2.UnitX),0); Step(w);
        w.Combat!.Move(w.Npc!.Id,p.Position - Vector2.UnitY * 0.5f);
        Assert.True(w.Combat.ExecuteNpcAttack(w.Npc.Id,1,Vector2.UnitY,w.Tick));
        Assert.Equal(0,w.Combat.Get(p.EntityId).Health); Step(w,12);
        Assert.Equal(0,Skill(w,p,3).Practice);
        w.TryQueueProgression(42,new(1,ProgressionAction.AssignSlot,0,1,8)); Step(w);
        Assert.Equal(ProgressionOutcome.InvalidState,w.ProgressionResults[p.EntityId].Outcome);
    }
    private static ServerWorld World(bool durableTarget = false)
    {
        var catalog = ContentCatalogTests.Load();
        if (durableTarget)
        {
            var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            foreach (var creature in json["creatures"]!.AsArray()) creature!["baseHealth"] = 10000;
            catalog = ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        }
        return new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,inventory:Options.Create(new InventoryOptions { Enabled = true }));
    }
    private static void Step(ServerWorld w,int count = 1) { for (var i=0;i<count;i++) w.Simulate(0.05f); }
    private static SkillProgress Skill(ServerWorld w,ServerPlayer p,ushort id) => w.ProgressionState(p.EntityId,w.Tick).Skills.Single(s => s.Id == id);
    [Fact]
    public void EmptyRejectedCooldownAndReplayDoNotAwardExperienceOrPractice()
    {
        var w = World(); var p = w.AddPlayer(42,new(1));
        w.TryQueueAbility(42,new(1,0,999,Vector2.UnitY),0); Step(w);
        w.TryQueueAbility(42,new(2,0,1,Vector2.UnitX),0); Step(w,80);
        Assert.False(w.TryQueueAbility(42,new(2,0,1,Vector2.UnitY),0)); Step(w);
        Assert.Equal(0,w.ProgressionState(p.EntityId,w.Tick).Experience); Assert.Equal(0,Skill(w,p,1).Practice);
        w.TryQueueAbility(42,new(3,0,1,Vector2.UnitY),0); Step(w,20);
        w.TryQueueAbility(42,new(4,0,1,Vector2.UnitY),0); Step(w);
        Assert.Equal(AbilityOutcome.Cooldown,w.Abilities!.Results[p.EntityId].Outcome); Step(w,60);
        Assert.Equal(1,Skill(w,p,1).Practice); Assert.Equal(1,w.ProgressionState(p.EntityId,w.Tick).Experience);
        Step(w,80); Assert.Equal(1,Skill(w,p,1).Practice);
    }
    [Fact]
    public void SuccessfulUsesImproveSkillAndDashRequiresCompletedTravel()
    {
        var w = World(durableTarget:true); var p = w.AddPlayer(42,new(1));
        double firstDamage = 0;
        for (uint seq=1;seq<=4;seq++)
        {
            w.TryQueueAbility(42,new(seq,0,1,Vector2.UnitY),0);
            for (var i=0;i<70;i++) { Step(w); if (w.Abilities!.Hits.Count != 0) { if (seq == 1) firstDamage = w.Abilities.Hits[0].Damage; if (seq == 4) Assert.True(w.Abilities.Hits[0].Damage > firstDamage); } }
        }
        Assert.Equal(2,Skill(w,p,1).Level); Assert.Equal(1,Skill(w,p,1).Practice);
        w.TryQueueAbility(42,new(5,0,3,Vector2.UnitX),0); Step(w,12); Assert.Equal(1,Skill(w,p,3).Practice);
    }
    [Fact]
    public void DiscoveryIsOneTimeAcrossRestoreAndLearningNeedsItsSource()
    {
        var w = World(); var state = w.CreateInitialCharacter() with { X = -9,Z = -6 };
        var p = w.AddPlayer(42,new(1),state);
        w.TryQueueProgression(42,new(1,ProgressionAction.LearnSkill,0,5,0)); Step(w);
        Assert.Equal(ProgressionOutcome.Unavailable,w.ProgressionResults[p.EntityId].Outcome);
        w.TryApplyMove(42,new(1,0,new(-9,-5))); Step(w,30);
        Assert.Equal(60,w.ProgressionState(p.EntityId,w.Tick).Experience); Assert.True(Skill(w,p,5).Learnable);
        w.TryQueueProgression(42,new(2,ProgressionAction.LearnSkill,0,5,0)); Step(w);
        Assert.Equal(1,Skill(w,p,5).Level); Assert.Equal(0,Skill(w,p,5).Slot);
        w.TryQueueAbility(42,new(1,0,5,Vector2.UnitY),0); Step(w); Assert.Equal(AbilityOutcome.UnknownAbility,w.Abilities!.Results[p.EntityId].Outcome);
        var saved = w.CaptureCharacter(42); var next = World(); var restored = next.AddPlayer(42,new(1),saved);
        next.TryApplyMove(42,new(1,0,new(-9,-6))); Step(next,30);
        Assert.Equal(60,next.ProgressionState(restored.EntityId,next.Tick).Experience); Assert.Equal(1,Skill(next,restored,5).Level);
        saved = next.CaptureCharacter(42) with { X = 9,Z = 6 }; var third = World(); var final = third.AddPlayer(42,new(1),saved);
        third.TryApplyMove(42,new(1,0,new(9,5))); Step(third,30);
        var result = third.ProgressionState(final.EntityId,third.Tick); Assert.Equal(2,result.Level); Assert.Equal(20,result.Experience); Assert.Equal(3,result.StatPoints);
    }
    [Fact]
    public void StatAllocationKeepsEquipmentHealthManaAndCooldownWhileReplayAndFloodAreRejected()
    {
        var w = World(); var initial = w.CreateInitialCharacter();
        var armor = initial.Inventory!.Items.Single(i => ContentCatalogTests.Load().Items[i.DefinitionId].Slot == EquipmentSlot.Armor);
        initial = initial with { Health = 12,Mana = 7,AttackCooldownSeconds = 60,
            Cooldowns = initial.Cooldowns.Select(c => c with { Seconds = 60 }).ToArray(),
            Inventory = initial.Inventory with { Items = initial.Inventory.Items.Select(i => i == armor ? i with { EquippedSlot = EquipmentSlot.Armor } : i).ToArray() },
            Progression = initial.Progression! with { StatPoints = 3 } };
        var p = w.AddPlayer(42,new(1),initial); var hpMax = w.Combat!.Get(p.EntityId).Stats.MaxHealth;
        w.TryQueueProgression(42,new(1,ProgressionAction.AllocateStat,2,0,0)); Step(w);
        var saved = w.CaptureCharacter(42); Assert.Equal(initial.Stats.Vitality+1,saved.Stats.Vitality); Assert.Equal(12,saved.Health); Assert.Equal(7,saved.Mana);
        Assert.True(w.Combat.Get(p.EntityId).Stats.MaxHealth > hpMax); Assert.Contains(saved.Inventory!.Items,i => i.EquippedSlot == EquipmentSlot.Armor); Assert.All(saved.Cooldowns,c => Assert.InRange(c.Seconds,58,60));
        Assert.False(w.TryQueueProgression(42,new(1,ProgressionAction.AllocateStat,2,0,0)));
        w.TryQueueProgression(42,new(2,ProgressionAction.AllocateStat,2,0,0));
        for (uint i=3;i<1000;i++) Assert.False(w.TryQueueProgression(42,new(i,ProgressionAction.AllocateStat,2,0,0)));
        Step(w); Assert.Equal(2,w.ProgressionState(p.EntityId,w.Tick).StatPoints); Assert.Equal(ProgressionOutcome.RateLimited,w.ProgressionResults[p.EntityId].Outcome);
    }
    [Fact]
    public void BarSwapDoesNotResetCooldownAndUnownedSkillsCannotBeAssigned()
    {
        var w = World(); var initial = w.CreateInitialCharacter(); initial = initial with { Cooldowns = initial.Cooldowns.Select(c => c with { Seconds = 60 }).ToArray() };
        var p = w.AddPlayer(42,new(1),initial);
        w.TryQueueProgression(42,new(1,ProgressionAction.AssignSlot,0,1,8)); Step(w); Assert.Equal(8,Skill(w,p,1).Slot);
        w.TryQueueProgression(42,new(2,ProgressionAction.AssignSlot,0,1,0)); Step(w); Assert.DoesNotContain(w.Abilities!.Loadout(p.EntityId,w.Tick).Abilities,a => a.Id == 1);
        w.TryQueueProgression(42,new(3,ProgressionAction.AssignSlot,0,1,1)); Step(w); Assert.InRange(w.Abilities.Loadout(p.EntityId,w.Tick).Abilities.Single(a => a.Id == 1).ReadyInSeconds,58,60);
        w.TryQueueProgression(42,new(4,ProgressionAction.AssignSlot,0,5,1)); Step(w); Assert.Equal(ProgressionOutcome.Unavailable,w.ProgressionResults[p.EntityId].Outcome);
    }
    [Fact]
    public void InvalidSavedModelsAndBalanceFailClosed()
    {
        var value = World().CreateInitialCharacter().Progression!;
        Assert.Throws<InvalidDataException>(() => SavedProgression.Deserialize(value.Serialize().Replace("\"Version\":1","\"Version\":99")));
        Assert.Throws<InvalidDataException>(() => SavedProgression.Deserialize(value.Serialize().Replace("\"Version\":1","\"Version\":1,\"Version\":1")));
        Assert.Throws<InvalidDataException>(() => (value with { Skills = [value.Skills[0],value.Skills[0]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (value with { Skills = [value.Skills[0] with { Slot = 9 }] }).Validate());
        var w = World(); var initial = w.CreateInitialCharacter();
        Assert.Throws<InvalidDataException>(() => w.AddPlayer(42,new(1),initial with { Progression = value with { Skills = [value.Skills[0] with { DefinitionId = "missing" }] } })); Assert.Empty(w.Players);
        var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!; json["progression"]!["experiencePerLevel"] = 0;
        Assert.Throws<InvalidDataException>(() => ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
