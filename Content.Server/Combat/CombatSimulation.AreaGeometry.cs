using System.Numerics;
using Content.Server.Data;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal bool GroundContactVisible(Vector3 bottom,float radius,GroundAreaDefinition volume,Combatant target)
    {
        if(!new GroundCylinder(bottom,radius,volume.Height).TryContact(target.Hurtbox,out var witness))return false;
        if(volume.RequiresConnectedSurface && !_navigation.TraverseSurface(bottom,new(target.Position.X,target.Height,target.Position.Y)))return false;
        var center=bottom+Vector3.UnitY*(volume.Height*.5f);
        return BodyContactVisible(center,witness,0,target.Hurtbox);
    }
}
