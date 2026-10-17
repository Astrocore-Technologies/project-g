using System.Numerics;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class SkillChargeIntegrationTests
{
    [Fact]
    public void SeparateUsesKeepFirstTimerAndResetRestoresEveryCharge()
    {
        var f=new Fight(); f.Use(1); var first=f.Skill;
        Assert.Equal(2,first.Charges); Assert.Equal(0,first.ReadyInSeconds);
        f.Step(10); f.Use(2);
        Assert.Equal(1,f.Skill.Charges); Assert.True(f.Skill.RechargeInSeconds<first.RechargeInSeconds-.4);
        Assert.True(f.Abilities.ResetCooldown(f.Id,1)); Assert.Equal(3,f.Skill.Charges); Assert.Equal(0,f.Skill.RechargeInSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelDoesNotSpendChargeButInterruptionSpendsOne(bool interrupted)
    {
        var f=new Fight(cast:true); f.Use(1); Assert.Equal(3,f.Skill.Charges);
        if(interrupted) f.Abilities.InterruptCast(f.Id);
        else { Assert.True(f.World.TryQueueActionCancel(42,new(1,1))); f.Step(); }
        Assert.Equal(interrupted ? 2 : 3,f.Skill.Charges);
        Assert.Equal(interrupted ? 137.5 : 140,f.Abilities.Mana(f.Id));
        Assert.Equal(interrupted ? 5 : 0,f.Skill.RechargeInSeconds,5);
    }

    [Fact]
    public void FullCdrAllowsZeroCooldownWhileActivePhaseStillOwnsExecution()
    {
        var f=new Fight(active:.2); f.Cdr(1); f.Use(1);
        Assert.Equal(0,f.Skill.CooldownSeconds); Assert.Equal(3,f.Skill.Charges);
        Assert.True(f.World.TryQueueAbility(42,new(2,0,1,Vector2.UnitX),0)); f.Step();
        Assert.Equal(AbilityOutcome.Buffered,f.Abilities.Results[f.Id].Outcome);
        f.Step(5); f.Use(3); Assert.Equal(3,f.Skill.Charges);
    }

    [Fact]
    public void EquipmentCdrDoesNotRestartOrShortenTheCurrentChargeInterval()
    {
        var f=new Fight(); f.Use(1); f.Step(10); var remaining=f.Skill.RechargeInSeconds;
        f.Cdr(.5); Assert.Equal(remaining,f.Skill.RechargeInSeconds,8); Assert.Equal(2.5,f.Skill.CooldownSeconds);
        f.Use(2);
        for(var i=0;i<110 && f.Skill.Charges==1;i++) f.Step();
        Assert.Equal(2,f.Skill.Charges); Assert.InRange(f.Skill.RechargeInSeconds,2.44,2.51);
    }

    [Fact]
    public void SerializedCharacterRetainsMissingChargesAndOfflineSequentialRecovery()
    {
        var f=new Fight(); f.Use(1); f.Use(2); f.Use(3);
        var saved=f.World.CaptureCharacter(42); var slot=saved.Cooldowns.Single(c=>c.AbilityId=="test_projectile");
        Assert.Equal((byte)0,slot.AvailableCharges!.Value); Assert.Equal(5d,slot.RechargeDuration!.Value);
        saved=CharacterState.Deserialize(saved.Serialize()) with {SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.AddSeconds(-7).ToUnixTimeMilliseconds()};
        var resumed=new Fight(saved:saved);
        Assert.Equal(1,resumed.Skill.Charges); Assert.InRange(resumed.Skill.RechargeInSeconds,2.8,3.1);
        Assert.Equal(0,resumed.Skill.ReadyInSeconds);
    }

    [Theory]
    [InlineData(-1,5)] [InlineData(.5,2.5)] [InlineData(1,0)] [InlineData(10,0)]
    public void ExecutedCdrUsesApprovedBounds(double reduction,double expected)
    {
        var f=new Fight(); f.Cdr(reduction); Assert.Equal(expected,f.Skill.CooldownSeconds);
    }

    private sealed class Fight
    {
        internal readonly ServerWorld World;
        private readonly ServerPlayer _player;
        internal NetworkEntityId Id=>_player.EntityId;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal AbilityProfile Skill=>Abilities.Loadout(Id,World.Tick).Abilities.Single(p=>p.Id==1);
        internal Fight(bool cast=false,double active=0,CharacterState? saved=null)
        {
            var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            var skill=data["abilities"]![0]!; skill["maxCharges"]=3; skill["cooldownSeconds"]=5;
            if(!cast) skill["action"]=JsonNode.Parse("{\"form\":\"Instant\",\"category\":\"Skill\",\"capabilities\":\"Magical\",\"active\":{\"seconds\":"+active.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}}");
            data["creatures"]![0]!["baseManaRecovery"]=0; data["balance"]!["manaRecovery"]!["wisdom"]=0;
            var catalog=ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>1);
            _player=World.AddPlayer(42,new(1),saved);
        }
        internal void Use(uint sequence)
        {
            Assert.True(World.TryQueueAbility(42,new(sequence,0,1,Vector2.UnitX),0)); Step();
            Assert.Equal(AbilityOutcome.Accepted,Abilities.Results[Id].Outcome);
        }
        internal void Step(int count=1) {for(var i=0;i<count;i++) World.Simulate(.05f);}
        internal void Cdr(double value)
        {
            var actor=World.Combat!.Get(Id); var stats=actor.Stats with {CooldownReduction=value};
            var next=Abilities.PrepareEquipment(Id,stats);
            World.Combat.ApplyEquipment(Id,(stats,actor.Weapon,actor.AttackInterval)); Abilities.ApplyEquipment(Id,next);
        }
    }
}
