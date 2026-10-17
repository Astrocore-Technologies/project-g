using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Development;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class DodgeTests
{
    internal static JsonNode Document()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["creatures"]![0]!["baseHealthRecovery"]=0;data["balance"]!["healthRecovery"]!["vitality"]=0;
        data["effects"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"dodge_probe_dot","durationSeconds":0.25,"disposition":"Harmful",
             "periodic":{"kind":"Damage","intervalSeconds":0.125,"power":{"flat":10},"damageFamily":"Physical"}}
            """));
        return data;
    }
    internal static ServerWorld World(JsonNode? data=null,NavigationOptions? navigation=null)
        =>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),navigation is null?null:Options.Create(navigation),
            inventory:Options.Create(new InventoryOptions{Enabled=true}),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes((data??Document()).ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
    private sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly ServerPlayer Player,Enemy;
        internal CombatSimulation Combat=>World.Combat!;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal NetworkEntityId Id=>Player.EntityId;
        internal DodgeProfileDefinition? Profession=>Combat.Get(Id).ProfessionDodge;
        internal AbilityProfile Profile=>Abilities.Loadout(Id,World.Tick).Abilities.Single(s=>s.Id==3);
        internal Fight(JsonNode? data=null,bool profession=false,int level=1,NavigationOptions? navigation=null)
        {
            var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes((data??Document()).ToJsonString()));
            World=DodgeTests.World(data,navigation);
            var initial=new BalanceTestBuild{Stats=catalog.Creatures["test_adventurer"].Stats,ProfessionId=profession?(ushort)2:(ushort)0,SkillLevel=level}.CreateCharacter(catalog,World.CreateInitialCharacter());
            Player=World.AddPlayer(42,new(1),initial with {X=-7,Z=0});
            Enemy=World.AddPlayer(43,new(2),World.CreateInitialCharacter() with {X=-10,Z=0});
            Combat.DamagePermission=(_,_)=>true;
            var actor=Combat.Get(Id);actor.BaseDerivedStats=actor.Stats=actor.Stats with {PhysicalDefense=0,MagicDefense=0};
        }
        internal void Cast(float distance=0,float delta=.05f)
        {
            Assert.True(World.TryQueueAbility(42,new(1,World.Tick,3,Vector2.UnitX,distance),0));World.Simulate(delta);
            Assert.Equal(AbilityOutcome.Accepted,Abilities.Results[Id].Outcome);
        }
        internal ResolvedHit Hit(bool magic=false)=>Combat.ResolveDirectHit(Enemy.EntityId,Id,Vector2.UnitX,10,magic);
    }

    [Theory][InlineData(false,1)][InlineData(true,1)][InlineData(false,10)][InlineData(true,10)]
    public void ApprovedProfilesPayOnceAndHaveFixedTimingRegardlessOfSkillLevel(bool profession,int level)
    {
        var f=new Fight(profession:profession,level:level);var profile=f.Profile;
        Assert.Equal(3,profile.Range);Assert.Equal(15,profile.Speed);Assert.Equal(20,profile.StaminaCost);Assert.Equal(3,profile.CooldownSeconds);
        Assert.Equal(.2,profile.DodgeDurationSeconds);Assert.Equal(.15,profile.DodgeProtectionSeconds);Assert.Equal(DodgeThreats.All,profile.DodgeProtectedThreats);
        f.Cast();Assert.Equal(80,f.Combat.AvailableStamina(f.Id));Assert.Equal(3,f.Profile.ReadyInSeconds);
        var state=Assert.Single(f.Abilities.ActiveStates);Assert.Equal(AbilityPhase.Dash,state.Phase);Assert.Equal(.2f,state.RemainingSeconds);
        Assert.Equal(f.Combat.Get(f.Id).Stats.MaxMana,f.Abilities.Mana(f.Id));
        f.World.Simulate(.25f);Assert.Null(f.Combat.Get(f.Id).Action);Assert.Equal(80,f.Combat.AvailableStamina(f.Id));
        Assert.InRange(Vector2.Distance(new(-7,0),f.Player.Position),2.999,3.001);
    }

    [Fact]public void ProfessionOverrideIsIndependentOfOrdinaryDodgeAndTheEquippedSword()
    {
        var data=Document();var tuning=data["professions"]![1]!["dodge"]!;
        tuning["range"]=4;tuning["durationSeconds"]=.4;tuning["staminaCost"]=25;tuning["cooldownSeconds"]=5;
        tuning["protectionSeconds"]=.1;tuning["protectedThreats"]="DirectPhysical";
        var ordinary=new Fight(data);var sword=new Fight(data,profession:true);
        Assert.Equal(3,ordinary.Profile.Range);Assert.Equal(20,ordinary.Profile.StaminaCost);
        Assert.Equal(4,sword.Profile.Range);Assert.Equal(10,sword.Profile.Speed);Assert.Equal(25,sword.Profile.StaminaCost);
        sword.Combat.SwordEquipped=_=>false;Assert.Equal(4,sword.Profile.Range);Assert.Equal(25,sword.Combat.DodgeCostFor(sword.Id));
        sword.Cast();Assert.Equal(GuardImpact.Dodged,sword.Hit().Guard);Assert.Equal(10,sword.Hit(magic:true).Damage);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ShortTravelOrAWallDoesNotShortenTheActionAndInitialProtection(bool wall)
    {
        var f=new Fight(navigation:wall?new(){BlockedAreas=[new(){X=9,Z=14,Width=1,Height=4}]}:null);
        f.Cast(wall?0:.5f);f.World.Simulate(.1f);
        Assert.False(f.Player.Motion.IsDashing);Assert.NotNull(f.Combat.Get(f.Id).Action);
        var position=f.Player.Position;Assert.InRange(Vector2.Distance(new(-7,0),position),.4,.6);
        Assert.Equal(GuardImpact.Dodged,f.Hit().Guard);
        f.World.Simulate(.1f);Assert.Equal(position,f.Player.Position);Assert.Null(f.Combat.Get(f.Id).Action);
        Assert.Equal(10,f.Hit().Damage);Assert.Equal(80,f.Combat.AvailableStamina(f.Id));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void DirectDamageIsPreventedOnlyInsideTheWindowWithoutChangingHp(bool magic)
    {
        var f=new Fight();f.Cast();var hp=f.Combat.Get(f.Id).Health;var hit=f.Hit(magic);
        Assert.Equal(GuardImpact.Dodged,hit.Guard);Assert.Equal(0,hit.Damage);Assert.False(hit.Evaded);Assert.False(hit.Critical);
        Assert.Equal(hp,f.Combat.Get(f.Id).Health);Assert.Contains(f.Combat.CurrentJournal(f.Id),e=>e.Kind==CombatJournalKind.Dodge);
        f.World.Simulate(.15f);hit=f.Hit(magic);Assert.Equal(GuardImpact.None,hit.Guard);Assert.Equal(10,hit.Damage);
    }

    [Fact]public void DodgePrecedesShieldAbsorptionAndDoesNotAvoidOwnHealthPricesOrSelfDamage()
    {
        var data=Document();data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"dodge_probe_shield","durationSeconds":2,"shield":{"power":{"flat":20}}}"""));
        var f=new Fight(data);f.Cast();Assert.True(f.Combat.ApplyEffect(f.Id,f.Id,"dodge_probe_shield"));
        var shield=Assert.Single(f.Combat.Get(f.Id).Effects.Values);var hp=f.Combat.Get(f.Id).Health;
        Assert.Equal(GuardImpact.Dodged,f.Hit().Guard);Assert.Equal(20,shield.ShieldRemaining);Assert.Equal(hp,f.Combat.Get(f.Id).Health);
        Assert.True(f.Combat.PayHealth(f.Id,5));Assert.Equal(hp-5,f.Combat.Get(f.Id).Health);Assert.Equal(20,shield.ShieldRemaining);
        Assert.Equal(0,f.Combat.SelfDamage(f.Id,5,EffectDamageFamily.Physical));Assert.Equal(15,shield.ShieldRemaining);
        f.World.Simulate(.15f);var hit=f.Hit();Assert.Equal(GuardImpact.None,hit.Guard);Assert.Equal(0,hit.Damage);Assert.Equal(5,shield.ShieldRemaining);
    }

    [Theory][InlineData(.01f)][InlineData(.05f)][InlineData(.25f)]
    public void PeriodicTicksKeepTheirScheduleAndOnlyProtectedTicksLoseTheirDamage(float delta)
    {
        var f=new Fight();f.Cast(delta:delta);var hp=f.Combat.Get(f.Id).Health;
        Assert.True(f.Combat.ApplyEffect(f.Enemy.EntityId,f.Id,"dodge_probe_dot"));
        f.World.Simulate(.25f);
        Assert.Equal(hp-10,f.Combat.Get(f.Id).Health);
        Assert.Single(f.Combat.EffectEvents,e=>e.Kind==CombatEffectEventKind.Dodge && e.Amount==10);
        Assert.Single(f.Combat.EffectEvents,e=>e.Kind==CombatEffectEventKind.PeriodicDamage && e.Amount==10);
        Assert.Empty(f.Combat.Get(f.Id).Effects);
    }

    [Theory][InlineData(CrowdControlKind.Root)][InlineData(CrowdControlKind.Stun)][InlineData(CrowdControlKind.Fear)]
    public void InterruptingControlUsesAcrAndImmediatelyEndsProtectionWithoutRefund(CrowdControlKind kind)
    {
        var f=new Fight();f.Cast();Assert.Equal(GuardImpact.Dodged,f.Hit().Guard);
        Assert.True(f.Combat.ApplyStatusControl(f.Enemy.EntityId,f.Id,1,100,kind,1));
        Assert.True(f.Combat.Get(f.Id).Controls.Has(kind));Assert.False(f.Player.Motion.IsDashing);
        Assert.True(f.Hit().Damage>0);Assert.Equal(80,f.Combat.AvailableStamina(f.Id));Assert.Equal(3,f.Profile.ReadyInSeconds);
        Assert.True(f.Combat.Get(f.Id).Controls.Ledger.Snapshot(AdaptiveControlLedger.Category(kind),f.Combat.Time).Used>0);
    }

    [Fact]public void AnAttackContactCanApplyControlDuringTheDamageProtectionWindow()
    {
        var data=Document();var skill=data["abilities"]![1]!;skill["power"]=1;skill["magicAttackScale"]=0;skill["radius"]=.5;
        skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
        skill["effects"]=JsonNode.Parse("""[{"operation":"Control","trigger":"Contact","target":"HitTarget","control":{"kind":"Root","durationSeconds":1}}]""");
        var f=new Fight(data);var hp=f.Combat.Get(f.Id).Health;
        Assert.True(f.World.TryQueueAbility(43,new(1,0,2,f.Player.Position),0));f.Cast();
        var hit=Assert.Single(f.Abilities.Hits);Assert.Equal(GuardImpact.Dodged,hit.Guard);Assert.Equal(0,hit.Damage);
        Assert.Equal(hp,f.Combat.Get(f.Id).Health);Assert.True(f.Combat.Get(f.Id).Controls.Has(CrowdControlKind.Root));
        Assert.False(f.Player.Motion.IsDashing);Assert.True(f.Hit().Damage>0);
    }

    [Fact]public void DisconnectKeepsTheActualDodgeRunningButRestartRestoresOnlyPaidState()
    {
        var f=new Fight();f.Cast();f.World.DisconnectPvp(42);
        Assert.True(f.Combat.Get(f.Id).DetachedBody);Assert.True(f.Player.Motion.IsDashing);Assert.Equal(GuardImpact.Dodged,f.Hit().Guard);
        var saved=f.World.CaptureCharacter(42) with {SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-1000};
        var restarted=World();var restored=restarted.AddPlayer(42,new(1),saved);var combat=restarted.Combat!;
        Assert.Equal(saved.X,restored.Position.X);Assert.Equal(saved.Z,restored.Position.Y);Assert.False(restored.Motion.IsDashing);
        Assert.Null(combat.Get(restored.EntityId).Action);Assert.Empty(restarted.Abilities!.ActiveStates);
        Assert.Equal(80,combat.AvailableStamina(restored.EntityId));
        var cooldown=restarted.Abilities.Loadout(restored.EntityId,0).Abilities.Single(s=>s.Id==3).ReadyInSeconds;
        Assert.InRange(cooldown,1.8,2.01);
        f.World.Simulate(.25f);Assert.True(Vector2.Distance(new(-7,0),f.Player.Position)>2.99);
    }

    [Fact]public void ZeroCooldownDoesNotBypassTheCurrentDodgeActionOrDuplicateItsCost()
    {
        var data=Document();data["abilities"]![2]!["cooldownSeconds"]=0;var f=new Fight(data);f.Cast();
        f.World.Simulate(.05f); // Submit inside the agreed 150 ms buffer before the 200 ms action ends.
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,3,Vector2.UnitX),0));f.World.Simulate(.05f);
        Assert.Equal(AbilityOutcome.Buffered,f.Abilities.Results[f.Id].Outcome);Assert.Equal(80,f.Combat.AvailableStamina(f.Id));
        f.World.Simulate(.0625f);Assert.Equal(80,f.Combat.AvailableStamina(f.Id));
        f.World.Simulate(.0625f);Assert.Equal(AbilityOutcome.Accepted,f.Abilities.Results[f.Id].Outcome);
        Assert.Equal(60,f.Combat.AvailableStamina(f.Id));Assert.Equal(2u,f.Combat.Get(f.Id).Action!.InputSequence);
    }
}
