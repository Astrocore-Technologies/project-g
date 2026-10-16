using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ReleasedProjectilePvpTests
{
    private sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly ServerPlayer Source,Target;
        internal readonly List<AbilityHit> Hits=[];
        internal readonly Guid Character=Guid.NewGuid();
        internal long Now=100000;
        internal Fight(PvpMode sourceMode=PvpMode.Criminal,PvpMode targetMode=PvpMode.Peaceful,long protection=0)
        {
            var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![0]!;
            data["creatures"]![0]!["baseHealthRecovery"]=0;
            data["balance"]!["healthRecovery"]!["vitality"]=0;
            skill["power"]=1000;skill["magicAttackScale"]=0;skill["speed"]=2;skill["range"]=6;skill["radius"]=.1;
            skill["action"]!["form"]="Instant";skill["action"]!["preparation"]!["seconds"]=0;skill["action"]!["active"]=new JsonObject();
            World=PvpTests.World(ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));World.PvpClock=()=>Now;
            Source=World.AddPlayer(42,new(1),Initial(-6,sourceMode));Target=World.AddPlayer(43,new(2),Initial(-3,targetMode,protection));
            World.BindWorldActor(42,Character);World.BindWorldActor(43,Guid.NewGuid());World.DeathRoll=_=>0;
        }
        private CharacterState Initial(float x,PvpMode mode,long protection=0)
        {
            var state=PvpTests.Initial(World,x,-5);
            return state with {Progression=state.Progression! with {Pvp=new SavedPvp{Mode=mode,ProtectionUntil=protection}}};
        }
        internal void Step(){World.Simulate(.05f);Hits.AddRange(World.Abilities!.Hits);}
        internal void Cast()
        {
            Assert.True(World.TryQueueAbility(42,new(1,World.Tick,1,Vector2.UnitX),0));Step();
            Assert.Equal(AbilityOutcome.Accepted,World.Abilities!.Results[Source.EntityId].Outcome);
        }
        internal void Finish(){for(var i=0;i<150 && World.Abilities!.ActiveStates.Count>0;i++)Step();Assert.Empty(World.Abilities!.ActiveStates);}
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void DeadOrRemovedCriminalSourceStillDamagesAndAttributesTheTargetsDeath(bool remove)
    {
        var f=new Fight();f.Cast();
        if(remove)f.World.RemovePlayer(42);
        else Assert.True(f.World.Combat!.ApplyAbilityDamage(f.Source.EntityId,100000,f.World.Npc!.Id)>0);
        f.Finish();Assert.True(Assert.Single(f.Hits).Damage>0);
        var saved=f.World.CaptureCharacter(43);Assert.Equal(0,saved.Health);Assert.True(saved.Progression!.Pvp!.PvpDeath);
        Assert.Equal(f.Character,saved.Progression.Pvp.LastAttacker);Assert.Single(f.World.CaptureWorldNode().DeathLoot!);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void PeacefulSourceCannotHitAPeacefulTargetEvenAfterRemoval(bool remove)
    {
        var f=new Fight(PvpMode.Peaceful);f.Cast();if(remove)f.World.RemovePlayer(42);f.Finish();
        Assert.Empty(f.Hits);Assert.Equal(20,f.World.Combat!.Get(f.Target.EntityId).Health);
        Assert.False(f.World.Combat.InCombat(f.Target.EntityId));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void TargetBecomingVoluntaryDuringFlightIsEvaluatedAtContact(bool remove)
    {
        var f=new Fight(PvpMode.Peaceful);f.Cast();if(remove)f.World.RemovePlayer(42);
        Assert.True(f.World.TryQueuePvp(43,new(1,PvpAction.Mode,PvpMode.Voluntary,false,0)));f.Step();
        Assert.Equal(PvpOutcome.Accepted,f.World.PvpResults[f.Target.EntityId].Outcome);f.Finish();Assert.Single(f.Hits);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void CurrentTargetProtectionIsRespectedAfterSourceRemoval(bool expire)
    {
        var f=new Fight(protection:101000);f.Cast();f.World.RemovePlayer(42);if(expire)f.Now=102000;f.Finish();
        Assert.Equal(expire?1:0,f.Hits.Count);Assert.Equal(!expire,f.World.Combat!.Get(f.Target.EntityId).Health>0);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ASourceStillInTheWorldUsesItsCurrentPositionOtherwiseTheReleaseOrigin(bool remove)
    {
        var f=new Fight();f.Cast();f.World.Combat!.Move(f.Source.EntityId,new(-9,0));
        if(remove)f.World.RemovePlayer(42);f.Finish();Assert.Equal(remove?1:0,f.Hits.Count);
    }
}
