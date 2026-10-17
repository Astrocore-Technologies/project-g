using Content.Shared.Network;
using Godot;
using V2=System.Numerics.Vector2;

namespace ProjectG.Combat;

/// <summary>Public stable body for choosing a 3D aim point. It never decides an authoritative hit.</summary>
public partial class CombatAimBody : Area3D
{
    public const uint Layer=1u<<4;
    public const uint MovementBodyLayer=1u<<1;
    private readonly CollisionShape3D _collision=new(){Disabled=true};
    private CombatBodyProfile _profile;
    private CombatControlPhase _control;
    private V2 _facing=V2.UnitY;
    private bool _alive=true;

    public override void _Ready()
    {
        CollisionLayer=Layer;CollisionMask=0;Monitoring=false;Monitorable=true;InputRayPickable=false;
        AddChild(_collision);
    }

    public void Configure(CombatBodyProfile profile)
    {
        if(!profile.IsValid)throw new ArgumentException("Invalid public combat body.");
        if(_profile==profile)return;
        _profile=profile;
        _collision.Shape=profile.Shape==CombatBodyShape.Capsule ? new CapsuleShape3D{Radius=profile.Radius,Height=profile.Height}:null;
        Refresh();
    }
    public void Pose(CombatControlPhase control,V2 facing)
    {
        if(_control==control && _facing==facing)return;
        var posture=(_control==CombatControlPhase.KnockedDown)!=(control==CombatControlPhase.KnockedDown);
        _control=control;_facing=facing;
        if(posture || control==CombatControlPhase.KnockedDown)Refresh();
    }
    public void Alive(bool alive){if(_alive==alive)return;_alive=alive;Refresh();}

    private void Refresh()
    {
        _collision.Disabled=!_alive || _profile.Shape==CombatBodyShape.None;
        if(_profile.Shape==CombatBodyShape.None)return;
        var prone=_control==CombatControlPhase.KnockedDown;
        // Avatar/NPC render roots are one metre above their interpolated foot,
        // already including airborne height. Keep this shape outside the animated rig.
        Position=new(0,(prone?_profile.Radius:_profile.Height*.5f)-1,0);
        Quaternion=Godot.Quaternion.Identity;
        if(prone)
        {
            var forward=new Vector3(_facing.X,0,_facing.Y);
            if(forward.LengthSquared()>.000001f)Quaternion=new(Vector3.Up.Cross(forward.Normalized()),Mathf.Pi*.5f);
        }
    }
}
