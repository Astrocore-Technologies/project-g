using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CombatFacingTests
{
    private static (ServerWorld World,ServerPlayer Player) Fight(ContentCatalog? catalog=null,CharacterState? saved=null)
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:catalog??ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);
        var player=world.AddPlayer(42,new(1),saved??world.CreateInitialCharacter() with {X=-7,Z=0});
        var actor=world.Combat!.Get(player.EntityId);
        world.Combat.ApplyEquipment(player.EntityId,(actor.BaseDerivedStats with {AttackSpeedMultiplier=1},actor.Weapon,1));
        return(world,player);
    }

    [Fact]public void ActualOrdinaryMotionUpdatesFacingAndStoppingRetainsIt()
    {
        var (world,player)=Fight();var actor=world.Combat!.Get(player.EntityId);
        Assert.True(world.TryApplyMove(42,new(1,0,player.Position+Vector2.UnitX)));
        world.Simulate(.125f);Assert.Equal(Vector2.UnitX,actor.Facing);Assert.Equal(actor.Facing,world.Avatar(player.EntityId).Facing);
        for(var i=0;i<8;i++)world.Simulate(.125f);
        Assert.False(player.Motion.IsMoving);Assert.Equal(Vector2.UnitX,actor.Facing);
        Assert.True(world.TryApplyMove(42,new(2,world.Tick,player.Position-Vector2.UnitY)));
        world.Simulate(.125f);Assert.Equal(-Vector2.UnitY,actor.Facing);
    }

    [Fact]public void BasicAimOwnsPreparationAndActiveFacingWhileRecoveryAllowsMovementToTurn()
    {
        var (world,player)=Fight();var actor=world.Combat!.Get(player.EntityId);
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitX)));Assert.Equal(Vector2.UnitY,actor.Facing);
        world.Simulate(.0625f);Assert.Equal(Vector2.UnitX,actor.Facing);
        Assert.True(world.TryApplyMove(42,new(1,world.Tick,player.Position+Vector2.UnitY*4)));
        world.Simulate(.201f);Assert.Equal(CombatActionPhase.Active,actor.Action!.Phase);Assert.Equal(Vector2.UnitX,actor.Facing);
        Assert.False(world.Combat.FaceIntent(player.EntityId,-Vector2.UnitY));
        world.Simulate(.1f);Assert.Equal(CombatActionPhase.Recovery,actor.Action.Phase);Assert.Equal(Vector2.UnitY,actor.Facing);
        Assert.True(world.Combat.TryBasicState(player.EntityId,world.Tick,out var state));Assert.Equal(Vector2.UnitY,state.Direction);
    }

    [Fact]public void AcceptedPreparationAimTurnsButRejectedAimAndDefenseDoNot()
    {
        var (world,player)=Fight();var combat=world.Combat!;var actor=combat.Get(player.EntityId);
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitX)));world.Simulate(.0625f);
        Assert.False(combat.UpdateBasicAim(player.EntityId,1,actor.Action!.ExecutionId,Vector2.Zero));Assert.Equal(Vector2.UnitX,actor.Facing);
        Assert.True(combat.UpdateBasicAim(player.EntityId,1,actor.Action.ExecutionId,-Vector2.UnitY));Assert.Equal(-Vector2.UnitY,actor.Facing);
        combat.RestoreDefense(player.EntityId,new(1,0,0,1));
        Assert.True(world.TryQueueDefense(42,new(1,DefenseAction.Parry,Vector2.UnitX)));world.Simulate(.0625f);
        Assert.Equal(DefenseOutcome.NoStamina,combat.DefenseState(player.EntityId,world.Tick).Outcome);Assert.Equal(-Vector2.UnitY,actor.Facing);
    }

    [Theory][InlineData(DefenseAction.Block)][InlineData(DefenseAction.Parry)]
    public void DefenseAimRetainsFacingWhileMovingAndReleaseRestoresMovementFacing(DefenseAction defense)
    {
        var (world,player)=Fight();var actor=world.Combat!.Get(player.EntityId);actor.ProfessionAllowsUnarmedBlock=true;
        Assert.True(world.TryQueueDefense(42,new(1,defense,-Vector2.UnitX)));world.Simulate(.0625f);
        Assert.True(world.TryApplyMove(42,new(1,world.Tick,player.Position+Vector2.UnitY*4)));
        world.Simulate(.0625f);Assert.Equal(-Vector2.UnitX,actor.Facing);
        if(defense==DefenseAction.Block)
        {Assert.True(world.TryQueueDefense(42,new(2,DefenseAction.Release,-Vector2.UnitX)));world.Simulate(.0625f);}
        else world.Simulate(.25f);
        world.Simulate(.0625f);Assert.Equal(Vector2.UnitY,actor.Facing);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ChargedGroundAimUpdatesFacingOnlyWhenItsPreparationAllowsTurning(bool turn)
    {
        var (world,player)=Fight(ChannelingTests.Parse(ChargedAbilityTests.Document(turn:turn)));var actor=world.Combat!.Get(player.EntityId);
        Assert.True(world.TryQueueAbility(42,new(1,0,2,player.Position+Vector2.UnitX),0));world.Simulate(.0625f);
        Assert.Equal(Vector2.UnitX,actor.Facing);
        Assert.True(world.TryQueueAbilityControl(42,new(1,1,world.Tick,AbilityControlKind.Aim,player.Position-Vector2.UnitY)));
        world.Simulate(.0625f);Assert.Equal(turn?-Vector2.UnitY:Vector2.UnitX,actor.Facing);
    }

    [Fact]public async Task SqliteAndRegionTransferRetainOrientationAndLegacySavesUseTheExistingDefault()
    {
        var (world,player)=Fight();Assert.True(world.Combat!.FaceIntent(player.EntityId,new(-.6f,-.8f)));
        var captured=world.CaptureCharacter(42);Assert.NotNull(captured.Facing);
        var frozen=Content.Server.Regions.FrozenCharacterState.Capture(captured).Restore();Assert.Equal(captured.Facing,frozen.Facing);
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",captured,default);var token=seed.IssuedToken;await seed.DisposeAsync();
        await using var reopened=await store.OpenAsync(token,world.CreateInitialCharacter(),default);
        var (restored,owner)=Fight(saved:reopened.State);
        Assert.True(Vector2.Distance(world.Combat.Get(player.EntityId).Facing,restored.Combat!.Get(owner.EntityId).Facing)<.000001);
        Assert.Equal(captured.Facing,restored.CaptureCharacter(42).Facing);
        var (legacy,old)=Fight(saved:captured with {Facing=null});Assert.Equal(Vector2.UnitY,legacy.Combat!.Get(old.EntityId).Facing);
    }

    [Theory][InlineData(0,1,0)][InlineData(2,1,0)][InlineData(1,0,0)][InlineData(1,2,0)]
    [InlineData(1,float.NaN,0)][InlineData(1,0,float.PositiveInfinity)]
    public void InvalidSavedOrientationIsRejected(int version,float x,float z)
    {
        var (world,_)=Fight();var saved=world.CreateInitialCharacter() with {Facing=new(version,x,z)};
        Assert.Throws<InvalidDataException>(()=>saved.Serialize());
    }
}
