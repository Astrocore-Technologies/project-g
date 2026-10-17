using Godot;
using ProjectG.Camera;
using ProjectG.Gameplay;

namespace ProjectG.Tests.Camera;

/// <summary>Real camera projection: pitch, percentage steps, bounds, local zoom and height following.</summary>
public partial class GameplayCameraSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var player = GD.Load<PackedScene>("res://Scenes/PlayerAvatar.tscn").Instantiate<PlayerController>();
            var rig = player.GetNode<GameplayCamera>("CameraRig");
            rig.PersistZoom = false; // Never modify the user's settings in a smoke test.
            player.SetPhysicsProcess(false);
            player.SetProcess(false);
            AddChild(player);
            var camera = rig.GetNode<Camera3D>("Camera3D");
            camera.Current = true;
            rig.Initialize(true);
            var listener=rig.GetNode<AudioListener3D>("AudioListener3D");
            Check(listener.IsCurrent() && GetViewport().GetAudioListener3D()==listener,"Local avatar must own the spatial audio listener");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(rig.ZoomPercent == 100, "Unexpected initial zoom");
            Check(Mathf.Abs(camera.RotationDegrees.X + 55) < .001f, "Pitch must be 55 degrees");
            foreach (var position in new[] { Vector3.Zero, new Vector3(10, 25, -30), new Vector3(-12, -4, 9) })
            {
                player.Position = position;
                foreach (var step in new[] { -1, 1, -12, 12, -6 })
                {
                    var previous = rig.ZoomPercent;
                    rig.ZoomBySteps(step);
                    Check(rig.ZoomPercent == Math.Clamp(previous + step * 5, 70, 130), "Zoom step or bound is wrong");
                    var expected = rig.StandardDistance * rig.ZoomPercent / 100f;
                    Check(Mathf.Abs(camera.Position.Length() - expected) < .0001f, "Zoom must change distance by percentage points");
                    var centre = GetViewport().GetVisibleRect().GetCenter();
                    Check(camera.UnprojectPosition(player.GlobalPosition).DistanceTo(centre) < .01f,
                        "Avatar must remain exactly centred after height/position/zoom changes");
                    Check(listener.GlobalPosition.DistanceTo(player.GlobalPosition)<.0001f,
                        "Camera zoom or world height moved the audio listener away from the avatar");
                    Check(listener.GlobalBasis.IsEqualApprox(camera.GlobalBasis),"Stereo orientation must match the camera");
                }
            }
            // Camera follows the root, not hit/death/turn transforms of the visual child.
            var before = camera.GlobalTransform;
            player.GetNode<Node3D>("Visual").Rotation = new(.4f, 1.2f, .3f);
            Check(camera.GlobalTransform == before, "Animation moved the camera");
            rig.ZoomBySteps(int.MaxValue);
            Check(rig.ZoomPercent == 130, "Upper bound overflow");
            var feedback=new ProjectG.Animation.CharacterFeedback();player.AddChild(feedback);
            feedback.Cue("hit");
            var voice=feedback.GetChildren().OfType<AudioStreamPlayer3D>().SingleOrDefault();
            Check(voice is {Playing:true},"Own sound was culled at maximum camera distance");
            Check(listener.GlobalPosition.DistanceTo(voice!.GlobalPosition)<voice.MaxDistance,"Own sound is outside the audible distance");
            var remote=GD.Load<PackedScene>("res://Scenes/PlayerAvatar.tscn").Instantiate<PlayerController>();
            remote.SetPhysicsProcess(false);remote.SetProcess(false);AddChild(remote);
            var remoteRig=remote.GetNode<GameplayCamera>("CameraRig");remoteRig.PersistZoom=false;remoteRig.Initialize(false);
            Check(GetViewport().GetAudioListener3D()==listener,"Remote avatar stole the local audio listener");
            remote.QueueFree();
            rig.ZoomBySteps(int.MinValue);
            Check(rig.ZoomPercent == 70, "Lower bound overflow");
            player.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("GAMEPLAY_CAMERA_SMOKE_OK: 55 degrees, 5% steps, 70-130%, exact centre at multiple heights, animation isolation.");
            GD.Print("ZOOM_AUDIO_OK: avatar listener at all heights/zoom levels, camera stereo, own sound at 130%, remote ownership isolation.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
