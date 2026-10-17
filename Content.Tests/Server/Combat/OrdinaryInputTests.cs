using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class OrdinaryInputTests
{
    private sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly NetworkEntityId Id;
        internal CombatSimulation Combat => World.Combat!;
        internal AbilitySimulation Skills => World.Abilities!;
        internal Combatant Actor => Combat.Get(Id);
        private uint _basic, _defense, _skill;
        internal Fight(bool lockedCast=false)
        {
            var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            data["creatures"]![0]!["baseManaRecovery"]=0;data["balance"]!["manaRecovery"]!["wisdom"]=0;
            if(lockedCast)
            {data["abilities"]![0]!["action"]!["preparation"]!["cancel"]="None";
             data["abilities"]![0]!["action"]!["preparation"]!["seconds"]=.14;}
            var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>1);
            Id=World.AddPlayer(42,new(1)).EntityId;Actor.ProfessionAllowsUnarmedBlock=true;
        }
        internal void Step(float delta=.05f)=>World.Simulate(delta);
        internal void Basic()=>Assert.True(World.TryQueueAttack(42,new(++_basic,World.Tick,Vector2.UnitX)));
        internal void Defense(DefenseAction action)=>Assert.True(World.TryQueueDefense(42,new(++_defense,action,Vector2.UnitX)));
        internal void Skill(ushort skill=1,int rtt=0)=>Assert.True(World.TryQueueAbility(42,new(++_skill,World.Tick,skill,
            skill==2 ? Actor.Position : Vector2.UnitX),rtt));
        internal DefenseState Guard=>Combat.DefenseState(Id,World.Tick);
        internal AbilityOutcome SkillOutcome=>Skills.Results[Id].Outcome;
        internal AttackOutcome BasicOutcome=>Combat.Results[Id].Outcome;
        internal void Lock(double seconds=.12)=>Combat.SetRecovery(Id,seconds);
    }

    [Theory][InlineData(DefenseAction.Block)][InlineData(DefenseAction.Parry)]
    public void DefenseWinsOverSkillAndBasicRegardlessOfPacketFamilyOrder(DefenseAction defense)
    {
        var f=new Fight();f.Skill();f.Defense(defense);f.Basic();f.Step();
        Assert.Equal(DefenseOutcome.Accepted,f.Guard.Outcome);Assert.Empty(f.Skills.ActiveStates);Assert.Empty(f.Combat.Events);
        Assert.Equal(AbilityOutcome.Busy,f.SkillOutcome);Assert.Equal(AttackOutcome.InvalidState,f.BasicOutcome);
        Assert.Equal(0,f.Combat.Inputs.WaitingCount);
    }

    [Fact]public void DodgeWinsOverBlockAndBasicAndPaysOnce()
    {
        var f=new Fight();f.Defense(DefenseAction.Block);f.Basic();f.Skill(3);f.Step();
        Assert.Equal(AbilityOutcome.Accepted,f.SkillOutcome);Assert.False(f.Guard.Blocking);Assert.Empty(f.Combat.Events);
        Assert.Equal(80,f.Guard.Stamina);Assert.Equal(CombatActionCategory.Dodge,f.Actor.Action!.Definition.Category);
    }

    [Fact]public void EqualPriorityUsesLatestReceiptWithoutExecutingBothParryAndDodge()
    {
        var f=new Fight();f.Skill(3);f.Defense(DefenseAction.Parry);f.Step();
        Assert.Equal(90,f.Guard.Stamina);Assert.True(f.Guard.ParryRemaining>0);Assert.Empty(f.Skills.ActiveStates);
        var g=new Fight();g.Defense(DefenseAction.Parry);g.Skill(3);g.Step();
        Assert.Equal(80,g.Guard.Stamina);Assert.Equal(0,g.Guard.ParryRemaining);Assert.Single(g.Skills.ActiveStates);
    }

    [Fact]public void RejectedHighPriorityDoesNotSuppressAValidSkill()
    {
        var f=new Fight();f.Combat.SpendStamina(f.Id,95);f.Skill();f.Defense(DefenseAction.Parry);f.Step();
        Assert.Equal(DefenseOutcome.NoStamina,f.Guard.Outcome);Assert.Equal(AbilityOutcome.Accepted,f.SkillOutcome);
        Assert.Single(f.Skills.ActiveStates);
    }

    [Fact]public void ParryIsEstablishedBeforeAnotherActorsCompetingFreshBasic()
    {
        var spatial=new SpatialIndex(8);var catalog=ContentCatalogTests.Load();
        var combat=new CombatSimulation(catalog,spatial,new NavigationOptions().CreateGrid(new MovementOptions().ToSettings()),
            new CombatOptions(),20,8,()=>1,()=>0);
        var attacker=new NetworkEntityId(1);var defender=new NetworkEntityId(2);
        combat.Add(attacker,Vector2.Zero,CombatEntityKind.Player);combat.Add(defender,Vector2.UnitY,CombatEntityKind.Player);
        spatial.Add(attacker,Vector2.Zero);spatial.Add(defender,Vector2.UnitY);combat.DamagePermission=(_,_)=>true;
        Assert.True(combat.Queue(attacker,new(1,0,Vector2.UnitY),0));
        Assert.True(combat.QueueDefense(defender,new(1,DefenseAction.Parry,-Vector2.UnitY),0));combat.Simulate(.05f,1);
        Assert.Empty(combat.Events);combat.Simulate(.2f,2);
        Assert.Equal(GuardImpact.Parried,Assert.Single(combat.Events).Guard);Assert.Equal(0,combat.Events[0].Damage);
    }

    [Fact]public void SkillWinsOverBasicButAnUnknownSkillDoesNotSuppressTheSwing()
    {
        var f=new Fight();f.Basic();f.Skill();f.Step();Assert.Empty(f.Combat.Events);Assert.Single(f.Skills.ActiveStates);
        var g=new Fight();g.Skill(65000);g.Basic();g.Step();Assert.Empty(g.Combat.Events);g.Step(.2f);Assert.Single(g.Combat.Events);
        Assert.Equal(AbilityOutcome.UnknownAbility,g.SkillOutcome);
    }

    [Fact]public void ManualBasicWaitsForItsWeaponIntervalThenExecutesExactlyOnce()
    {
        var f=new Fight();f.Actor.ReadyAt=f.Combat.Time+.12;f.Basic();f.Step();
        Assert.Equal(AttackOutcome.Buffered,f.BasicOutcome);Assert.Equal(1,f.Combat.Inputs.WaitingCount);Assert.Empty(f.Combat.Events);
        f.Step();Assert.Empty(f.Combat.Events);f.Step();Assert.Empty(f.Combat.Events);Assert.Equal(AttackOutcome.Accepted,f.BasicOutcome);
        f.Step(.2f);Assert.Single(f.Combat.Events);
        f.Step();Assert.Empty(f.Combat.Events);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
    }

    [Theory][InlineData(DefenseAction.Block)][InlineData(DefenseAction.Parry)]
    public void DefenseWaitsForALegalBoundaryWithoutSpendingEarly(DefenseAction defense)
    {
        var f=new Fight();f.Lock();f.Defense(defense);f.Step();
        Assert.Equal(DefenseOutcome.Buffered,f.Guard.Outcome);Assert.Equal(100,f.Guard.Stamina);Assert.False(f.Guard.Blocking);
        f.Step();f.Step();Assert.Equal(DefenseOutcome.Accepted,f.Guard.Outcome);
        Assert.Equal(defense==DefenseAction.Parry?90:99.85,f.Guard.Stamina,6);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
    }

    [Fact]public void BlockReleaseRemovesTheWaitingBlockBeforeTheWindowOpens()
    {
        var f=new Fight();f.Lock();f.Defense(DefenseAction.Block);f.Step();
        f.Defense(DefenseAction.Release);f.Step();f.Step();Assert.False(f.Guard.Blocking);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
    }

    [Fact]public void NewBasicReplacesAnOlderWaitingSkillInTheSameSingleSlot()
    {
        var f=new Fight();f.Lock();f.Skill();f.Step();Assert.Equal(AbilityOutcome.Buffered,f.SkillOutcome);
        f.Basic();f.Step();Assert.Equal(AttackOutcome.Buffered,f.BasicOutcome);Assert.Equal(AbilityOutcome.Busy,f.SkillOutcome);
        Assert.Equal(1,f.Combat.Inputs.WaitingCount);f.Step();Assert.Empty(f.Combat.Events);f.Step(.2f);Assert.Single(f.Combat.Events);Assert.Empty(f.Skills.ActiveStates);
    }

    [Fact]public void AnInvalidNewSkillDoesNotReplaceOrExtendAWaitingBasic()
    {
        var f=new Fight();f.Lock(.155);f.Basic();f.Step();f.Skill(65000);f.Step();
        Assert.Equal(AbilityOutcome.UnknownAbility,f.SkillOutcome);Assert.Equal(1,f.Combat.Inputs.WaitingCount);
        f.Step(.06f);Assert.Empty(f.Combat.Events);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
    }

    [Fact]public void ForbiddenCastCancellationWaitsAndDoesNotRefundOrCancelTheCast()
    {
        var f=new Fight(lockedCast:true);f.Skill();f.Step();var execution=f.Actor.Action!.ExecutionId;
        f.Skill(2);f.Step();Assert.Equal(AbilityOutcome.Buffered,f.SkillOutcome);Assert.Equal(execution,f.Actor.Action!.ExecutionId);
        Assert.Equal(140,f.Skills.Mana(f.Id));f.Step();f.Step();f.Step();
        Assert.Equal(AbilityOutcome.Accepted,f.SkillOutcome);Assert.Equal(2u,f.Actor.Action!.InputSequence);
        Assert.Contains(f.Skills.ActiveStates,s=>s.AbilityId==1&&s.Phase==AbilityPhase.Flying);Assert.Equal(135,f.Skills.Mana(f.Id));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ControlInvalidatesTheWaitingCommandEvenIfItExpiresBeforeTheNextStep(bool skill)
    {
        var f=new Fight();f.Lock();if(skill)f.Skill();else f.Basic();f.Step();
        f.Combat.Stun(f.Id,.01);f.Step(.1f);
        Assert.Empty(f.Combat.Events);Assert.Empty(f.Skills.ActiveStates);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
        if(skill)Assert.Equal(AbilityOutcome.InvalidState,f.SkillOutcome);else Assert.Equal(AttackOutcome.InvalidState,f.BasicOutcome);
    }

    [Fact]public void NoPrebufferThroughHardControlEvenWhenItEndsAtTheNextBoundary()
    {
        var f=new Fight();f.Combat.Stun(f.Id,.01);f.Skill();f.Step();
        Assert.Equal(AbilityOutcome.InvalidState,f.SkillOutcome);Assert.Empty(f.Skills.ActiveStates);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
    }

    [Fact]public void BufferedParryRechecksStaminaAtTheBoundary()
    {
        var f=new Fight();f.Lock();f.Defense(DefenseAction.Parry);f.Step();f.Combat.SpendStamina(f.Id,100);f.Step(.1f);
        Assert.Equal(DefenseOutcome.NoStamina,f.Guard.Outcome);Assert.Equal(0,f.Guard.ParryRemaining);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
    }

    [Fact]public void BufferedBlockRechecksItsWeaponPermission()
    {
        var f=new Fight();f.Lock();f.Defense(DefenseAction.Block);f.Step();f.Actor.ProfessionAllowsUnarmedBlock=false;
        f.Combat.ApplyEquipment(f.Id,f.Combat.PrepareEquipment(ContentCatalogTests.Load().Creatures["test_adventurer"].Stats,[]));f.Step(.1f);
        Assert.Equal(DefenseOutcome.NeedsWeapon,f.Guard.Outcome);Assert.False(f.Guard.Blocking);Assert.Equal(0,f.Combat.Inputs.WaitingCount);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ShortCapabilityControlDropsOnlyTheMatchingWaitingSkill(bool dodge)
    {
        var f=new Fight();f.Lock();f.Skill(dodge?(ushort)3:(ushort)1);f.Step();
        Assert.True(f.Combat.ApplyStatusControl(default,f.Id,0,0,dodge?CrowdControlKind.Root:CrowdControlKind.Silence,.01));
        f.Step(.1f);Assert.Equal(AbilityOutcome.InvalidState,f.SkillOutcome);Assert.Empty(f.Skills.ActiveStates);
    }

    [Theory][InlineData(0)][InlineData(200)][InlineData(2000)]
    public void TransportRttNeverExtendsTheWaitingDeadline(int rtt)
    {
        var f=new Fight();f.Lock(.155);f.Skill(rtt:rtt);f.Step();f.Step(.11f);
        Assert.Equal(AbilityOutcome.Busy,f.SkillOutcome);Assert.Empty(f.Skills.ActiveStates);Assert.Equal(140,f.Skills.Mana(f.Id));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void BasicAndDefenseUseTheSameExactOneHundredFiftyMillisecondWindow(bool defense)
    {
        var f=new Fight();f.Lock(.14);if(defense)f.Defense(DefenseAction.Parry);else f.Basic();f.Step();f.Step(.1f);
        if(defense)Assert.Equal(DefenseOutcome.Accepted,f.Guard.Outcome);else Assert.Equal(AttackOutcome.Accepted,f.BasicOutcome);
        Assert.Equal(0,f.Combat.Inputs.WaitingCount);
        var expired=new Fight();expired.Lock(.155);if(defense)expired.Defense(DefenseAction.Parry);else expired.Basic();expired.Step();expired.Step(.11f);
        Assert.Empty(expired.Combat.Events);Assert.Equal(0,expired.Guard.ParryRemaining);Assert.Equal(0,expired.Combat.Inputs.WaitingCount);
    }
}
