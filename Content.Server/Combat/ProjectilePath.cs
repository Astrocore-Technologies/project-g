using System.Numerics;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Absolute flight time avoids accumulating error at unrelated action/movement boundaries.</summary>
internal readonly record struct ProjectilePath(Vector3 Origin,Vector3 Direction,float Speed,float Radius,double Range,
    double StartedAt,double Offset,double EndsAt)
{
    internal Vector3 Velocity=>Direction*Speed;
    internal double Distance(double at)=>at>=EndsAt?Range:Math.Min(Range,Offset+Math.Max(0,at-StartedAt)*Speed);
    internal Vector3 Point(double distance)
    {
        var velocity=Velocity;var elapsed=distance/Speed;
        return new(Origin.X+(float)(velocity.X*elapsed),Origin.Y+(float)(velocity.Y*elapsed),Origin.Z+(float)(velocity.Z*elapsed));
    }
}

internal readonly record struct ProjectileContact(double At,NetworkEntityId Target,bool Obstacle,ulong HurtboxRevision,bool Prone=false);
