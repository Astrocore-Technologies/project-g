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
    public void Initialize(CombatState state, NavigationGrid grid)
        => Initialize(state.Position, state.ServerTick, grid, state.Height);
    public void Initialize(NumericsVector2 position, uint tick, NavigationGrid grid, float height = 0)
    {
        _grid = grid; _tick = tick;
        Position = new(position.X, height + 1, position.Y);
        _points.Add(new(Now(), position, height));
    }
    public void Apply(EntitySnapshot state, uint tick)
    {
        if (!MovementSimulation.IsSequenceNewer(tick, _tick)) return;
        _tick = tick; _points.Add(new(Now(), state.Position, state.Height));
        if (_points.Count > 20) _points.RemoveAt(0);
    }
    public override void _Process(double delta)
    {
        if (_points.Count == 0) return;
        var time = Now() - 0.1;
        while (_points.Count >= 2 && _points[1].Time <= time) _points.RemoveAt(0);
        var position = _points[0].Position; var height = _points[0].Height;
        if (_points.Count >= 2 && _grid.TraverseSurface(new(position.X, height, position.Y), new(_points[1].Position.X, _points[1].Height, _points[1].Position.Y)))
        {
            var amount = (float)Math.Clamp((time - _points[0].Time) / Math.Max(0.0001, _points[1].Time - _points[0].Time), 0, 1);
            position = NumericsVector2.Lerp(position, _points[1].Position, amount); height = float.Lerp(height, _points[1].Height, amount);
        }
        Position = new(position.X, height + 1, position.Y);
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;
    private readonly record struct Point(double Time, NumericsVector2 Position, float Height);
}
