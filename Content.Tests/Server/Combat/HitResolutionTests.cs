using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class HitResolutionTests
{
    private static CombatSimulation Combat(Func<double> hit, Func<double> critical)
    {
        var spatial=new SpatialIndex(8);
        var grid=new NavigationOptions().CreateGrid(new MovementOptions().ToSettings());
        var combat=new CombatSimulation(ContentCatalogTests.Load(),spatial,grid,new CombatOptions(),20,8,critical,hit);
        combat.Add(new(1),Vector2.Zero,CombatEntityKind.Player);
        combat.Add(new(2),Vector2.UnitY,CombatEntityKind.Monster);
        // Hit ordering is tested with an explicit instant-contact weapon; production phases have separate scenarios.
        foreach(var id in new[]{new NetworkEntityId(1),new NetworkEntityId(2)})
            combat.Get(id).Weapon=combat.Get(id).Weapon with {BasicAttack=null};
        spatial.Add(new(1),Vector2.Zero); spatial.Add(new(2),Vector2.UnitY);
        return combat;
    }

    [Fact]
    public void GeometricMissDoesNotRollNumericEvasionOrCritical()
    {
        var combat=Combat(()=>throw new Exception("Evasion was rolled without geometry"),()=>throw new Exception("Crit was rolled without geometry"));
        Assert.True(combat.Queue(new(1),new(1,0,-Vector2.UnitY),0)); combat.Simulate(.05f,1);
        var miss=Assert.Single(combat.Events); Assert.False(miss.TargetId.IsValid); Assert.False(miss.Evaded); Assert.Equal(0,miss.Damage);
    }

    [Fact]
    public void NumericEvasionPreventsDamageAndDoesNotConsumeParryOrRollCrit()
    {
        var combat=Combat(()=>.999,()=>throw new Exception("Evaded hit rolled crit"));
        Assert.True(combat.QueueDefense(new(1),new(1,DefenseAction.Parry,Vector2.UnitY),0)); combat.Simulate(.05f,1);
        var health=combat.Get(new(1)).Health; var stamina=combat.DefenseState(new(1),1).Stamina;
        Assert.True(combat.ExecuteNpcAttack(new(2),1,-Vector2.UnitY,1));
        var hit=Assert.Single(combat.Events); Assert.True(hit.Evaded); Assert.Equal(new NetworkEntityId(1),hit.TargetId);
        Assert.Equal(health,combat.Get(new(1)).Health); Assert.Equal(stamina,combat.DefenseState(new(1),1).Stamina);
        Assert.True(combat.DefenseState(new(1),1).ParryRemaining>0);
    }

    [Fact]
    public void ParryPrecedesCritAndGroundMagicCannotUseMeleeParry()
    {
        var combat=Combat(()=>0,()=>throw new Exception("Parried hit rolled crit"));
        combat.QueueDefense(new(1),new(1,DefenseAction.Parry,Vector2.UnitY),0); combat.Simulate(.05f,1);
        Assert.True(combat.ExecuteNpcAttack(new(2),1,-Vector2.UnitY,1));
        Assert.Equal(GuardImpact.Parried,Assert.Single(combat.Events).Guard);
        var magic=Combat(()=>0,()=>1); magic.QueueDefense(new(1),new(1,DefenseAction.Parry,Vector2.UnitY),0); magic.Simulate(.05f,1);
        var resolved=magic.ResolveDirectHit(new(2),new(1),-Vector2.UnitY,10,magic:true);
        Assert.True(resolved.Damage>0); Assert.Equal(GuardImpact.None,resolved.Guard);
        Assert.True(magic.DefenseState(new(1),1).ParryRemaining>0);
    }

    [Fact]
    public void CriticalDamageUsesDexterityForPhysicalAndMagicHits()
    {
        var combat=Combat(()=>0,()=>0); var target=combat.Get(new(2));
        target.Stats=target.Stats with {PhysicalDefense=0,MagicDefense=0};
        var physical=combat.ResolveDirectHit(new(1),new(2),Vector2.UnitY,10,magic:false);
        var magic=combat.ResolveDirectHit(new(1),new(2),Vector2.UnitY,10,magic:true);
        Assert.True(physical.Critical); Assert.True(magic.Critical);
        Assert.Equal(15,physical.Damage,10); Assert.Equal(physical.Damage,magic.Damage);
        var catalog=ContentCatalogTests.Load();
        combat.Get(new(1)).Stats=new Content.Server.Stats.StatCalculator(catalog.Balance)
            .Calculate(catalog.Creatures["test_adventurer"] with {Stats=new(0,0,0,0,10,0,0)});
        var dexPhysical=combat.ResolveDirectHit(new(1),new(2),Vector2.UnitY,10,magic:false);
        var dexMagic=combat.ResolveDirectHit(new(1),new(2),Vector2.UnitY,10,magic:true);
        Assert.Equal(16,dexPhysical.Damage,10);Assert.Equal(dexPhysical.Damage,dexMagic.Damage);
    }
}
