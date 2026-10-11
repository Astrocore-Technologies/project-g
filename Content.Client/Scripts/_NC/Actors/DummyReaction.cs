using Godot;

namespace ProjectG.Actors;

/// <summary>Visual spring reaction on the existing editable wooden dummy, leaving its target anchor intact.</summary>
public partial class DummyReaction : Node3D
{
    private double _age=1;
    private bool _alive=true;
    public void Hit() { if(_alive) _age=0; }
    public void SetAlive(bool alive) { _alive=alive; if(alive) _age=1; }
    public override void _Process(double delta)
    {
        _age+=delta;
        Rotation=new(_alive?(float)(Math.Sin(_age*28)*Math.Exp(-_age*9)*.16):.45f,0,0);
    }
}
