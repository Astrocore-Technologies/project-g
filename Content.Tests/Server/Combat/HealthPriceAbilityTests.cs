using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class HealthPriceAbilityTests
{
    private sealed class Fight(bool fatal=false,bool projectile=false)
    {
        internal readonly ServerWorld World=Create(fatal,projectile);
        internal NetworkEntityId Id;
        internal readonly List<AbilityHit> Hits=[];
        internal Combatant Actor=>World.Combat!.Get(Id);
        internal AbilityProfile Skill=>World.Abilities!.Loadout(Id,World.Tick).Abilities.Single(s=>s.Id==(projectile?1:2));
        private static ServerWorld Create(bool allowFatal,bool flying)
        {
            var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            var skill=data["abilities"]![flying?0:1]!;
            skill["healthCost"]=20;skill["allowFatalHealthCost"]=allowFatal;skill["manaCost"]=0;
            skill["power"]=10;skill["magicAttackScale"]=0;
            skill["action"]!["preparation"]!["seconds"]=.2;skill["action"]!["preparation"]!["speed"]="None";
            return new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
                catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
        }
        internal void Begin(double health=30)
        {
            var target=World.Combat!.Get(World.TrainingTargetId);
            Id=World.AddPlayer(42,new(1),World.CreateInitialCharacter() with {X=target.Position.X,Z=target.Position.Y-3,Health=health}).EntityId;
            var actor=Actor;World.Combat.ApplyEquipment(Id,(actor.BaseDerivedStats with {HealthRecovery=0},actor.Weapon,actor.AttackInterval));
            World.Combat.DamagePermission=(_,_)=>true;
            Assert.True(World.TryQueueAbility(42,new(1,World.Tick,Skill.Id,projectile?Vector2.UnitY:target.Position),0));Step();
            Assert.Equal(AbilityOutcome.Accepted,World.Abilities!.Results[Id].Outcome);
        }
        internal void Step(){World.Simulate(.05f);Hits.AddRange(World.Abilities!.Hits);}
        internal void Until(Func<bool> done){for(var i=0;i<100&&!done();i++)Step();Assert.True(done());}
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void CastCancellationAndEnemyInterruptionUseZeroAndHalfHp(bool enemy)
    {
        var f=new Fight();f.Begin();Assert.Equal(30,f.Actor.Health);
        if(enemy)f.World.Abilities!.InterruptCast(f.Id);
        else{Assert.True(f.World.TryQueueActionCancel(42,new(1,1)));f.Step();}
        Assert.Equal(enemy?20:30,f.Actor.Health);Assert.Empty(f.Hits);
        Assert.Equal(enemy,f.Skill.Charges==0);
    }
    [Fact]public void FailedActivationPaysOnlyAvailableNonfatalHpAndStartsCooldown()
    {
        var f=new Fight();f.Begin();f.Actor.Health=5;
        f.Until(()=>f.Skill.Charges==0);
        Assert.InRange(f.Actor.Health,double.Epsilon,1e-10);Assert.False(f.Actor.LifeDead);
        Assert.Empty(f.Hits);Assert.Equal(AbilityOutcome.NoHealth,f.World.Abilities!.Results[f.Id].Outcome);
    }
    [Fact]public void NonfatalHpPriceCannotStartAtExactCost()
    {
        var f=new Fight();var target=f.World.Combat!.Get(f.World.TrainingTargetId);
        var player=f.World.AddPlayer(42,new(1),f.World.CreateInitialCharacter() with {Health=20});
        var actor=f.World.Combat.Get(player.EntityId);
        f.World.Combat.ApplyEquipment(player.EntityId,(actor.Stats with {HealthRecovery=0},actor.Weapon,actor.AttackInterval));
        Assert.True(f.World.TryQueueAbility(42,new(1,0,2,target.Position),0));f.Step();
        Assert.Equal(AbilityOutcome.NoHealth,f.World.Abilities!.Results[player.EntityId].Outcome);
        Assert.Empty(f.Hits);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void FatalPaymentStillReleasesThePaidMainPayload(bool projectile)
    {
        var f=new Fight(fatal:true,projectile:projectile);f.Begin(20);
        f.Until(()=>f.Actor.LifeDead);Assert.Equal(0,f.Actor.Health);Assert.Equal(0,f.Skill.Charges);
        f.Until(()=>f.Hits.Any(h=>h.Damage>0));
        Assert.Single(f.Hits,h=>h.Damage>0);
    }
}
