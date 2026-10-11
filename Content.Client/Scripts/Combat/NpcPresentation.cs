using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace ProjectG.Combat;

/// <summary>Bounded interpolation only; NPC perception and navigation stay on the server.</summary>
public partial class NpcPresentation : Node3D
{
    private readonly List<Point> _points = new();
    private NavigationGrid _grid = null!;
    private uint _tick;
    public ProjectG.Actors.ActorVisual Visual { get; private set; } = null!;
    public ProjectG.Actors.NpcActorAnimation Animation { get; private set; } = null!;
    private double _controlUntil;
    private CombatControlPhase _control;
    public void Initialize(CombatState state, NavigationGrid grid)
    {
        Visual=GetNode<ProjectG.Actors.ActorVisual>("Figure");
        Animation=new ProjectG.Actors.NpcActorAnimation(); AddChild(Animation);
        Animation.Bind(Visual,state.ServerTick,state.Kind==CombatEntityKind.Boss);
        Initialize(state.Position,state.ServerTick,grid,state.Height);
        Animation.SetAlive(state.Health>0);
    }
    public void Initialize(NumericsVector2 position, uint tick, NavigationGrid grid, float height = 0)
    {
        _grid = grid; _tick = tick;
        Position = new(position.X, height + 1, position.Y);
        _points.Add(new(Now(), position, height));
    }
    public void Apply(EntitySnapshot state, uint tick)
    {
        if (!MovementSimulation.IsSequenceNewer(tick, _tick)) return;
        _control=state.Control; _controlUntil=Now()+state.ControlRemaining;
        if(_control!=CombatControlPhase.None) Animation.Stop();
        _tick = tick; _points.Add(new(Now(), state.Position, state.Height,state.AirOffset));
        if (_points.Count > 20) _points.RemoveAt(0);
    }
    public override void _Process(double delta)
    {
        if (_points.Count == 0) return;
        var time = Now() - 0.1;
        while (_points.Count >= 2 && _points[1].Time <= time) _points.RemoveAt(0);
        var position = _points[0].Position; var height = _points[0].Height; var air = _points[0].Air;
        if (_points.Count >= 2 && _grid.TraverseSurface(new(position.X, height, position.Y), new(_points[1].Position.X, _points[1].Height, _points[1].Position.Y)))
        {
            var amount = (float)Math.Clamp((time - _points[0].Time) / Math.Max(0.0001, _points[1].Time - _points[0].Time), 0, 1);
            position = NumericsVector2.Lerp(position, _points[1].Position, amount); height = float.Lerp(height, _points[1].Height, amount); air=float.Lerp(air,_points[1].Air,amount);
        }
        Position = new(position.X, height + air + 1, position.Y);
        if(Visual is not null) Visual.SetControl(_control,Math.Max(0,_controlUntil-Now()));
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;
    private readonly record struct Point(double Time, NumericsVector2 Position, float Height, float Air=0);
}
