using System.Numerics;
using System.Text.Json.Nodes;
using Content.Server.Persistence;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class PassiveAbilityTests
{
    internal static JsonNode Document()
    {
        var data=ChannelingTests.Document();
        var passive=JsonNode.Parse("""
            {"id":"test_passive","networkId":610,"kind":"Passive","power":0,"manaCost":0,"cooldownSeconds":0,
             "action":{"form":"Passive","category":"Skill"},"range":0,"radius":0,"speed":0,"magicAttackScale":0,
             "passive":{"modifiers":{"magicAttack":10}}}
            """);
        // Keep the authored definition separate from mutable character state.
        data["abilities"]!.AsArray().Add(passive);
        data["creatures"]![0]!["abilityIds"]!.AsArray().Add("test_passive");return data;
    }
    private static JsonNode Passive(JsonNode data)=>data["abilities"]!.AsArray().Last()!["passive"]!;

    [Fact]public void LearnedPassiveNeedsNoSlotAndCannotBeManuallyExecutedOrAssigned()
    {
        var f=new ChannelingTests.Fight(Document());var basePower=f.Actor.BaseDerivedStats.MagicAttack;
        Assert.Equal(basePower+10,f.Actor.Stats.MagicAttack);Assert.Null(f.Actor.Action);Assert.Empty(f.Abilities.ActiveStates);
        var progress=f.World.ProgressionState(f.Id,0);var passive=Assert.Single(progress.Skills,s=>s.Id==610);
        Assert.True(passive.IsPassive);Assert.Equal(0,passive.Slot);
        Assert.DoesNotContain(f.Abilities.Loadout(f.Id,0).Abilities,s=>s.Id==610);
        var mana=f.Mana;
        Assert.True(f.World.TryQueueAbility(42,new(1,0,610,Vector2.UnitX),0));f.Step();
        Assert.Equal(AbilityOutcome.UnknownAbility,f.Abilities.Results[f.Id].Outcome);Assert.Equal(mana,f.Mana);Assert.Null(f.Actor.Action);
        Assert.True(f.World.TryQueueProgression(42,new(1,ProgressionAction.AssignSlot,0,610,1)));f.Step();
        Assert.Equal(ProgressionOutcome.Unavailable,f.World.ProgressionResults[f.Id].Outcome);
    }

    [Fact]public void LearningRemovingAndReapplyingTheSamePassiveDoesNotDuplicateItsModifiersOrHeal()
    {
        var f=new ChannelingTests.Fight(Document());var progression=f.World.CaptureCharacter(42).Progression!;
        var hp=f.Actor.Health;var baseline=f.Actor.BaseDerivedStats.MagicAttack;var refreshed=0;
        f.Combat.EffectStatsChanged+=_=>refreshed++;
        f.Abilities.ApplyProgression(f.Id,progression);f.Abilities.ApplyProgression(f.Id,progression);
        Assert.Equal(baseline+10,f.Actor.Stats.MagicAttack);Assert.Equal(0,refreshed);
        f.Abilities.ApplyProgression(f.Id,progression with {Skills=progression.Skills.Where(s=>s.DefinitionId!="test_passive").ToArray()});
        Assert.Equal(baseline,f.Actor.Stats.MagicAttack);f.Abilities.ApplyProgression(f.Id,progression);
        Assert.Equal(baseline+10,f.Actor.Stats.MagicAttack);Assert.Equal(hp,f.Actor.Health);Assert.Equal(2,refreshed);
        for(var i=0;i<50;i++)f.Step();Assert.Equal(2,refreshed);
    }

    [Fact]public void WeaponConditionsUseThePreparedProfileAndRemoveAllBrokenWeaponBenefits()
    {
        var data=Document();Passive(data)["conditions"]=new JsonObject{["weaponIds"]=new JsonArray("training_sword")};
        var f=new ChannelingTests.Fight(data,weapon:true);var baseline=f.Actor.BaseDerivedStats.MagicAttack;
        Assert.Equal(baseline+10,f.Actor.Stats.MagicAttack);
        var next=f.Combat.PrepareEquipment(f.Player.BaseStats,[]);
        Assert.Equal(next.Stats.MagicAttack,f.Combat.WithEffects(f.Id,next.Stats,next.Weapon).MagicAttack);
        f.Combat.ApplyEquipment(f.Id,next);Assert.Equal(next.Stats.MagicAttack,f.Actor.Stats.MagicAttack);
    }

    [Fact]public void ProfessionAndEnvironmentConditionsCombineAndUpdateWhenTheirInputsChange()
    {
        var data=Document();var profession=data["professions"]![0]!["id"]!.GetValue<ushort>();
        Passive(data)["conditions"]=new JsonObject{["professionIds"]=new JsonArray(JsonValue.Create(profession)),
            ["requiredEnvironmentTags"]=new JsonArray("forest"),["excludedEnvironmentTags"]=new JsonArray("safe")};
        var f=new ChannelingTests.Fight(data);var baseline=f.Actor.BaseDerivedStats.MagicAttack;
        f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>{"forest"});Assert.Equal(baseline,f.Actor.Stats.MagicAttack);
        f.Combat.SetProfession(f.Id,profession);Assert.Equal(baseline+10,f.Actor.Stats.MagicAttack);
        f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>{"forest","safe"});Assert.Equal(baseline,f.Actor.Stats.MagicAttack);
        f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>{"forest"});Assert.Equal(baseline+10,f.Actor.Stats.MagicAttack);
        f.Combat.SetProfession(f.Id,0);Assert.Equal(baseline,f.Actor.Stats.MagicAttack);
    }

    [Theory][InlineData(true)][InlineData(false)]
    public void CombatConditionActivatesOnConfirmedContactAndExpiresWithoutAScanOfIdleActors(bool inCombat)
    {
        var data=Document();Passive(data)["conditions"]=new JsonObject{["inCombat"]=inCombat};
        var f=new ChannelingTests.Fight(data);long now=100000;f.World.PvpClock=()=>now;var baseline=f.Actor.BaseDerivedStats.MagicAttack;
        Assert.Equal(baseline+(inCombat?0:10),f.Actor.Stats.MagicAttack);
        f.Combat.ApplyAbilityDamage(f.Id,1,f.World.TrainingTargetId);
        Assert.True(f.Combat.InCombat(f.Id));Assert.Equal(baseline+(inCombat?10:0),f.Actor.Stats.MagicAttack);
        now+=30001;f.Step();Assert.False(f.Combat.InCombat(f.Id));Assert.Equal(baseline+(inCombat?0:10),f.Actor.Stats.MagicAttack);
    }

    [Fact]public void AliveConditionChangesOnDeathAndRevivalAndRespawnUsesTheLivingMaximum()
    {
        var data=Document();Passive(data)["conditions"]=new JsonObject{["alive"]=true};
        Passive(data)["modifiers"]=new JsonObject{["maxHealth"]=100};
        var f=new ChannelingTests.Fight(data);var baseline=f.Actor.BaseDerivedStats.MaxHealth;
        f.Combat.ApplyAbilityDamage(f.Id,100000,f.World.TrainingTargetId);Assert.Equal(baseline,f.Actor.Stats.MaxHealth);
        var death=f.World.CaptureCharacter(42).Life!.DeathId;
        Assert.True(f.Combat.Resurrect(default,f.Id,death,baseline+50));Assert.Equal(baseline+50,f.Actor.Health);Assert.Equal(baseline+100,f.Actor.Stats.MaxHealth);
        f.Combat.ApplyAbilityDamage(f.Id,100000,f.World.TrainingTargetId);f.Combat.Respawn(f.Id);
        Assert.Equal((baseline+100)*.5,f.Actor.Health);Assert.Equal(baseline+100,f.Actor.Stats.MaxHealth);
    }

    [Fact]public void PassiveBlockPermissionIsRemovedImmediatelyWhenItsConditionStops()
    {
        var data=Document();Passive(data)["allowsUnarmedBlock"]=true;
        Passive(data)["conditions"]=new JsonObject{["requiredEnvironmentTags"]=new JsonArray("forest")};
        var f=new ChannelingTests.Fight(data);f.Combat.ApplyEquipment(f.Id,f.Combat.PrepareEquipment(f.Player.BaseStats,[]));
        f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>{"forest"});
        Assert.True(f.World.TryQueueDefense(42,new(1,DefenseAction.Block,Vector2.UnitY)));f.Step();Assert.True(f.Combat.DefenseState(f.Id,0).Blocking);
        f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>());Assert.False(f.Combat.DefenseState(f.Id,0).Blocking);
    }

    [Fact]public void AuthoredPeriodicCriticalPermissionIsCapturedInReleasedSourceStats()
    {
        var data=Document();Passive(data)["allowsPeriodicCriticals"]=true;
        Passive(data)["conditions"]=new JsonObject{["requiredEnvironmentTags"]=new JsonArray("forest")};
        var f=new ChannelingTests.Fight(data);Assert.False(f.Combat.CaptureReleasedSource(f.Id,1).PeriodicCriticalsAllowed);
        f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>{"forest"});Assert.True(f.Combat.CaptureReleasedSource(f.Id,1).PeriodicCriticalsAllowed);
        f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>());Assert.False(f.Combat.CaptureReleasedSource(f.Id,1).PeriodicCriticalsAllowed);
    }

    [Fact]public void PassiveModifierScalingUsesTheExplicitLearnedLevelCurve()
    {
        var data=Document();Passive(data)["scaleModifiersWithSkillLevel"]=true;
        data["abilities"]!.AsArray().Last()!["progression"]=JsonNode.Parse("""{"levels":[{"practiceToNext":2,"powerMultiplier":0.5},{"practiceToNext":0,"powerMultiplier":3}]}""");
        var f=new ChannelingTests.Fight(data);var baseline=f.Actor.BaseDerivedStats.MagicAttack;Assert.Equal(baseline+5,f.Actor.Stats.MagicAttack);
        var progression=f.World.CaptureCharacter(42).Progression!;
        f.Abilities.ApplyProgression(f.Id,progression with {Skills=progression.Skills.Select(s=>s.DefinitionId=="test_passive"?s with {Level=2,Practice=0}:s).ToArray()});
        Assert.Equal(baseline+30,f.Actor.Stats.MagicAttack);
    }

    [Fact]public void CreatureUsesTheSamePassiveConditionsAndStatPipelineWithoutAPlayerLoadout()
    {
        var data=Document();Passive(data)["conditions"]=new JsonObject{["alive"]=true};Passive(data)["modifiers"]=new JsonObject{["maxHealth"]=100};
        data["creatures"]![1]!["abilityIds"]!.AsArray().Add("test_passive");
        var f=new ChannelingTests.Fight(data);var target=f.Combat.Get(f.World.TrainingTargetId);
        // Fight gives its dummy a test health pool; the underlying creature still owns the authored passive.
        Assert.Single(target.Passives);Assert.Equal(target.BaseDerivedStats.MaxHealth+100,f.Combat.WithEffects(target.Id,target.BaseDerivedStats).MaxHealth);
        f.Combat.ApplyAbilityDamage(target.Id,100000,f.Id);Assert.Equal(target.BaseDerivedStats.MaxHealth,target.Stats.MaxHealth);
        Assert.True(f.Combat.Resurrect(default,target.Id,f.Combat.CaptureLife(target.Id).DeathId,50));
        Assert.Equal(target.BaseDerivedStats.MaxHealth+100,target.Stats.MaxHealth);
    }

    [Theory][InlineData(false,10)][InlineData(true,20)]
    public void AuthoredPassivePermissionControlsActualDotCriticalsAndTheAppliedSnapshotSurvivesItsRemoval(bool enabled,double expected)
    {
        var data=Document();Passive(data)["allowsPeriodicCriticals"]=true;
        Passive(data)["conditions"]=new JsonObject{["requiredEnvironmentTags"]=new JsonArray("forest")};
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"passive_dot","disposition":"Harmful","durationSeconds":0.1,"periodic":{"kind":"Damage","damageFamily":"Magical","intervalSeconds":0.1,"power":{"flat":10}}}"""));
        var f=new ChannelingTests.Fight(data,criticalRoll:()=>0);var target=f.Combat.Get(f.World.TrainingTargetId);
        f.Combat.ApplyEquipment(f.Id,(f.Actor.BaseDerivedStats with {CriticalChance=1,CriticalDamage=2},f.Actor.Weapon,f.Actor.AttackInterval));
        if(enabled)f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>{"forest"});
        f.Combat.ApplyEffect(f.Id,target.Id,"passive_dot");f.Combat.SetEnvironmentTags(f.Id,new HashSet<string>());
        var hp=target.Health;f.World.Simulate(.11f);Assert.Equal(expected,hp-target.Health,8);
    }

    [Fact]public async Task RestoringConditionalMaximaUsesSavedContextButNeverResumesAnExpiredConditionOrRefillsResources()
    {
        var data=Document();Passive(data)["conditions"]=new JsonObject{["inCombat"]=true};
        Passive(data)["modifiers"]=new JsonObject{["maxHealth"]=100,["maxMana"]=100};
        var f=new ChannelingTests.Fight(data);f.Combat.ApplyAbilityDamage(f.Id,1,f.World.TrainingTargetId);
        f.Actor.Health=f.Actor.Stats.MaxHealth-5;f.Combat.RestoreMana!(f.Id,100);
        var saved=f.World.CaptureCharacter(42);Assert.Equal("test_passive",Assert.Single(saved.ActivePassiveModifiers!));
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var session=await store.OpenAsync("",saved,default);var token=session.IssuedToken;await store.SaveAsync([new(session,saved)],default);await session.DisposeAsync();
        await using var reopened=await store.OpenAsync(token,saved,default);
        var active=new ChannelingTests.Fight(data,saved:reopened.State);Assert.Equal(saved.Mana,active.Mana);Assert.Equal(saved.Health,active.Actor.Health);
        var expired=new ChannelingTests.Fight(data,saved:reopened.State with {SavedAtUnixMilliseconds=saved.SavedAtUnixMilliseconds-40000});
        Assert.False(expired.Combat.InCombat(expired.Id));
        Assert.Equal(saved.Health/saved.ResourceMaxima!.Health*expired.Actor.Stats.MaxHealth,expired.Actor.Health,8);
        Assert.Equal(expired.Actor.BaseDerivedStats.MaxMana,expired.Mana);Assert.Empty(expired.World.CaptureCharacter(42).ActivePassiveModifiers!);
    }

    [Fact]public void ChannelKeepsPayingEveryFundedPulseWhenItsFirstContactActivatesAPassiveAndRefreshesResources()
    {
        var data=Document();Passive(data)["conditions"]=new JsonObject{["inCombat"]=true};
        var f=new ChannelingTests.Fight(data,delta:.25f);f.Cast();f.Finish();
        Assert.Equal(10,f.Hits.Count);Assert.True(f.Combat.InCombat(f.Id));Assert.Equal(95,f.Mana,8);
        Assert.Equal(0,f.Skill.PendingEndCharges);Assert.InRange(f.Skill.ReadyInSeconds,4.9,5);
    }

    [Theory][InlineData("missing")][InlineData("unexpected")][InlineData("empty")][InlineData("weapon")][InlineData("profession")]
    [InlineData("contradiction")][InlineData("duration")][InlineData("cost")][InlineData("cooldown")][InlineData("contact")]
    public void InvalidPassiveContentIsRejectedBeforeWorldStartup(string scenario)
    {
        var data=Document();var skill=data["abilities"]!.AsArray().Last()!;var passive=Passive(data);
        switch(scenario)
        {
            case "missing":skill.AsObject().Remove("passive");break;
            case "unexpected":skill["action"]!["form"]="Instant";break;
            case "empty":passive["modifiers"]=new JsonObject();break;
            case "weapon":passive["conditions"]=new JsonObject{["weaponIds"]=new JsonArray("unknown")};break;
            case "profession":passive["conditions"]=new JsonObject{["professionIds"]=new JsonArray(ushort.MaxValue)};break;
            case "contradiction":passive["conditions"]=new JsonObject{["requiredEnvironmentTags"]=new JsonArray("forest"),["excludedEnvironmentTags"]=new JsonArray("forest")};break;
            case "duration":skill["action"]!["active"]=new JsonObject{["seconds"]=1};break;
            case "cost":skill["manaCost"]=1;break;
            case "cooldown":skill["cooldownSeconds"]=1;break;
            case "contact":skill["effects"]=new JsonArray(new JsonObject{["operation"]="Heal",["target"]="Self",["trigger"]="Activated",["power"]=new JsonObject{["flat"]=1}});break;
        }
        Assert.Throws<InvalidDataException>(()=>ChannelingTests.Parse(data));
    }

    [Fact]public void SavedPassiveContextRejectsUnknownUnlearnedAndDuplicateSources()
    {
        var data=Document();var f=new ChannelingTests.Fight(data);var saved=f.World.CaptureCharacter(42);
        Assert.Throws<InvalidDataException>(()=>new ChannelingTests.Fight(data,saved:saved with {ActivePassiveModifiers=["unknown"]}));
        Assert.Throws<InvalidDataException>(()=>(saved with {ActivePassiveModifiers=["test_passive","test_passive"]}).Serialize());
        var progression=saved.Progression! with {Skills=saved.Progression!.Skills.Where(s=>s.DefinitionId!="test_passive").ToArray()};
        Assert.Throws<InvalidDataException>(()=>new ChannelingTests.Fight(data,saved:saved with {Progression=progression}));
        Assert.Throws<InvalidDataException>(()=>new ChannelingTests.Fight(data,saved:saved with
            {Progression=saved.Progression! with {Skills=saved.Progression!.Skills.Select(s=>s.DefinitionId=="test_passive"?s with {Slot=8}:s).ToArray()}}));
    }
}
