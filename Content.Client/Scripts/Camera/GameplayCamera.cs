using Godot;
using ProjectG.UI;

namespace ProjectG.Camera;

/// <summary>Fixed, centred PC camera. Only the current camera accepts or saves zoom input.</summary>
public partial class GameplayCamera : Node3D
{
    public const int MinimumZoomPercent = 70;
    public const int MaximumZoomPercent = 130;
    public const int ZoomStepPercent = 5;
    private const string SettingsPath = "user://settings/camera.cfg";

    [Export(PropertyHint.Range, "1,89,0.1")] public float PitchDegrees { get; set; } = 55;
    // Preserve the former (0,24,24) camera distance as the prototype's editable 100% reference.
    [Export(PropertyHint.Range, "1,100,0.1")] public float StandardDistance { get; set; } = 33.941124f;
    [Export] public bool PersistZoom { get; set; } = true;
    public int ZoomPercent { get; private set; } = 100;
    private Camera3D _camera = null!;
    private AudioListener3D _listener = null!;
    private bool _local;

    public override void _Ready()
    {
        if (!float.IsFinite(PitchDegrees) || PitchDegrees is <= 0 or >= 90 ||
            !float.IsFinite(StandardDistance) || StandardDistance <= 0)
            throw new InvalidOperationException("GameplayCamera needs a pitch between 0 and 90 degrees and a positive distance.");
        _camera = GetNode<Camera3D>("Camera3D");
        _listener = GetNode<AudioListener3D>("AudioListener3D");
        // A child rig follows the authoritative/predicted avatar root, never its animated visual pose.
        Position = Vector3.Zero;
        Rotation = Vector3.Zero;
        Scale = Vector3.One;
        ApplyZoom();
    }

    public void Initialize(bool local)
    {
        _local = local;
        // Remote avatars share the scene but must not perform settings I/O or consume camera input.
        if (local && PersistZoom)
        {
            using var settings = new ConfigFile();
            if (settings.Load(SettingsPath) == Error.Ok)
            {
                var value = settings.GetValue("camera", "zoom_percent", 100);
                if (value.VariantType == Variant.Type.Int)
                    ZoomPercent = (int)Math.Clamp(value.AsInt64(), MinimumZoomPercent, MaximumZoomPercent);
            }
        }
        ZoomPercent = SnapZoom(ZoomPercent);
        ApplyZoom();
        if (local && _camera.Current) _listener.MakeCurrent();
        else if (_listener.IsCurrent()) _listener.ClearCurrent();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_listener) && _listener.IsCurrent()) _listener.ClearCurrent();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_local || !_camera.Current || !GetWindow().HasFocus() || GameUi.GameplayModalOpen ||
            GetViewport().GuiGetHoveredControl() is not null || PcInput.Blocked(this))
            return;
        var steps = PcInput.Pressed("zoom_in",@event) ? -1 : PcInput.Pressed("zoom_out",@event) ? 1 : 0;
        if (steps == 0) return;
        ZoomBySteps(steps);
        GetViewport().SetInputAsHandled();
    }

    public void ZoomBySteps(int steps)
    {
        // Clamp before multiplication, including calls from tests or a future remapped action.
        var next = Math.Clamp(ZoomPercent + Math.Clamp(steps, -12, 12) * ZoomStepPercent,
            MinimumZoomPercent, MaximumZoomPercent);
        if (next == ZoomPercent) return;
        ZoomPercent = next;
        ApplyZoom();
        if (PersistZoom && _local && _camera.Current)
        {
            var directory = ProjectSettings.GlobalizePath("user://settings");
            var error = DirAccess.MakeDirRecursiveAbsolute(directory);
            if (error != Error.Ok) { GD.PushWarning($"Could not create camera settings directory: {error}"); return; }
            using var settings = new ConfigFile();
            settings.SetValue("camera", "zoom_percent", ZoomPercent);
            error = settings.Save(SettingsPath);
            if (error != Error.Ok) GD.PushWarning($"Could not save camera settings: {error}");
        }
    }

    private static int SnapZoom(int percent) => MinimumZoomPercent +
        (int)Math.Round((percent - MinimumZoomPercent) / (double)ZoomStepPercent) * ZoomStepPercent;

    private void ApplyZoom()
    {
        var pitch = Mathf.DegToRad(PitchDegrees);
        var distance = StandardDistance * ZoomPercent / 100f;
        _camera.Position = new(0, Mathf.Sin(pitch) * distance, Mathf.Cos(pitch) * distance);
        _camera.Rotation = new(-pitch, 0, 0);
        // Hear from the avatar, with camera-oriented stereo. Zoom moves only the camera.
        _listener.Rotation = _camera.Rotation;
    }
}
