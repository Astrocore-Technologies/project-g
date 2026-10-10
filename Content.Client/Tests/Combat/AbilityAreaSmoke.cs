using Content.Shared.Navigation;
using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.Networking;
using V2 = System.Numerics.Vector2;
using V3 = System.Numerics.Vector3;

namespace ProjectG.Tests.Combat;

/// <summary>Actual hold/release input over UDP plus native rendering of bounded footprints and stacked floors.</summary>
public partial class AbilityAreaSmoke : Node
{
    private AbilityLoadout _loadout;
    private int _results;
    private AbilityResult _lastResult;
    private NetworkClient _network = null!;
    private AbilityPresentation _abilities = null!;
    private PlayerController _player = null!;

    public override async void _Ready()
    {
        try
        {
            GetWindow().Size = new(1280, 720);
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();
            world.AutoConnect = false; AddChild(world);
            _network = world.GetNode<NetworkClient>("NetworkClient");
            _network.AbilityLoadoutReceived += Loadout;
            _network.AbilityResultReceived += Result;
            _network.ConnectToServer();
            await Wait(() => _network.RegionActive && _loadout.Abilities is not null && _network.LatestDefense is not null, "baseline");
            _player = world.GetChildren().OfType<PlayerController>().Single(p => p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            _abilities = _player.GetChildren().OfType<AbilityPresentation>().Single();
            var dash = _loadout.Abilities.Single(a => a.Form == AbilityForm.Dash);
            Require(dash.Range == 20 && dash.CooldownSeconds == 20, "Swordsman dash balance");
            Require(_loadout.Abilities.All(a => a.Area.IsValid && a.Area.Shape != AbilityAreaShape.None), "server footprints");
            Aim(new(15, 0, -13)); await Delay(.15);
            var stamina = _network.LatestDefense!.Value.Stamina;
            var mana = _loadout.Mana;

            // Every equipped shape, including the unavailable riposte, can be inspected without casting.
            foreach (var entry in new[] { (Key.Q, AbilityAreaShape.Corridor), (Key.W, AbilityAreaShape.Sector),
                (Key.S, AbilityAreaShape.Sector), (Key.D, AbilityAreaShape.Self), (Key.Space, AbilityAreaShape.Corridor) })
            {
                Press(entry.Item1); await Delay(.15);
                Require(_abilities.IsAiming && _abilities.AreaVisual.Visible && _abilities.AreaVisual.Shape == entry.Item2, $"preview {entry.Item1}");
                Require(_abilities.AreaVisual.VertexCount is > 0 and <= 1200, "bounded mesh");
                Require(_abilities.AreaVisual.GlobalTransform.IsEqualApprox(Transform3D.Identity), "world-space footprint has no caster offset");
                GD.Print($"AREA_PREVIEW {entry.Item1}: {_abilities.AreaVisual.VertexCount} vertices, {_abilities.AreaVisual.GetChildren().OfType<MeshInstance3D>().Single().Mesh.GetAabb()}");
                if (entry.Item1 == Key.W) await Capture("sector");
                if (entry.Item1 == Key.Space) await Capture("dash");
                Press(Key.Escape); Release(Key.Escape); Release(entry.Item1); await Delay(.1);
                Require(!_abilities.IsAiming && !_abilities.AreaVisual.Visible, "Escape cancellation");
            }
            Require(_results == 0 && _loadout.Mana == mana && _network.LatestDefense.Value.Stamina == stamina, "hold/cancel spends nothing");
            Press(Key.Q); await Delay(.1);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            Release(Key.Q); await Delay(.15);
            Require(!_abilities.IsAiming && _results == 0, "RMB cancellation");
            Press(Key.Q); await Delay(.2); Require(_results == 0, "no cast while held");
            Release(Key.Q); await Wait(() => _results == 1, "release result");
            Require(_lastResult.Outcome == AbilityOutcome.Accepted && !_abilities.IsAiming, "release casts once");
            await Delay(.6);
            Press(Key.Q); await Delay(.15); Require(_abilities.AreaVisual.Visible, "cooldown still shows footprint");
            Release(Key.Q); await Delay(.2); Require(_results == 1, "cooldown release sends no cast");
            // Focus loss must not turn the eventual release into an accidental cast.
            Press(Key.W); await Delay(.1); GetWindow().EmitSignal(Window.SignalName.FocusExited); Release(Key.W);
            await Delay(.15); Require(!_abilities.IsAiming && _results == 1, "focus cancellation");
            CheckSurfaceRendering(dash);
            _network.AbilityLoadoutReceived -= Loadout; _network.AbilityResultReceived -= Result;
            world.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("ABILITY_AREA_OK: hold/release, Escape/RMB/focus cancellation, no cost before release, cooldown, all shapes, stacked floors and occlusion.");
            GetTree().Quit();
        }
        catch (Exception error) { await Capture("failure"); GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void CheckSurfaceRendering(AbilityProfile template)
    {
        var visual = new AbilityAreaVisual(); AddChild(visual);
        var grid = new NavigationGrid(new(new(-16, -16), 1, .45f, 32, 32, new byte[1024]));
        var map = new SurfaceGeometry
        {
            ScenePath = "res://Scenes/Regions/Smoke.tscn", SourceHash = new string('0', 64),
            Vertices = [new(-10, 0, -10), new(10, 0, -10), new(10, 0, 10), new(-10, 0, 10),
                new(-10, 4, -10), new(10, 4, -10), new(10, 4, 10), new(-10, 4, 10)],
            Polygons = [[0, 1, 2, 3], [4, 5, 6, 7]],
            Occluders = [new(new(0, 3.8f, 0), new(20, .2f, 20), new(0, 0, 0))]
        };
        map.Seal(); grid.AttachSurface(map);
        var circle = template with { Form = AbilityForm.GroundArea, Area = new(AbilityAreaShape.Circle, 2) };
        foreach (var height in new[] { 0f, 4f })
        {
            visual.Show(circle, grid, new(0, height, 0), new(0, height, 0), V2.UnitY, 0, default, true);
            Require(visual.VertexCount > 0, "circle rendering");
            var mesh = visual.GetChildren().OfType<MeshInstance3D>().Single().Mesh;
            var vertices = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Require(vertices.All(p => Math.Abs(p.Y - height - AbilityAreaVisual.SurfaceLift) < .001), "footprint stays on selected floor");
        }
        var projectile = template with { Form = AbilityForm.Projectile, Area = new(AbilityAreaShape.Corridor, 5, .5f) };
        visual.Show(projectile, grid, V3.Zero, V3.UnitZ * 5, V2.UnitY, 0, default, true);
        Require(visual.VertexCount == 6, "projectile corridor");
        var blocked = new NavigationGrid(new(new(-16, -16), 1, .45f, 32, 32, Enumerable.Repeat((byte)1, 1024).ToArray()));
        visual.Show(circle, blocked, V3.Zero, V3.Zero, V2.UnitY, 0, default, true);
        Require(!visual.Visible && visual.VertexCount == 0, "empty footprint without Godot errors");
        Require(!grid.ClearAttack(V3.Zero, new(0, 4, 0)), "floor occludes vertical attack");
        visual.QueueFree();
    }
    private void Loadout(AbilityLoadout value) => _loadout = value;
    private void Result(AbilityResult value) { _lastResult = value; _results++; }
    private static void Require(bool condition, string stage) { if (!condition) throw new InvalidOperationException(stage); }
    private static void Press(Key key) => Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
    private static void Release(Key key) => Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
    private void Aim(Vector3 point) => Input.WarpMouse(GetViewport().GetCamera3D().UnprojectPosition(point));
    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> done, string stage)
    {
        var end = Time.GetTicksMsec() + 20000;
        while (!done()) { if (Time.GetTicksMsec() > end) throw new TimeoutException(stage); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture")) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/skill-areas/" + name + ".png");
    }
}
