using Godot;

namespace ProjectG.Regions;

/// <summary>Offline camera inspection only. No character simulation or server connection.</summary>
public partial class BlockoutPreview : Node3D
{
    [Export] public Camera3D Camera { get; set; } = null!;
    [Export] public Label Status { get; set; } = null!;
    [Export] public Node3D Viewpoints { get; set; } = null!;
    [Export] public Node3D OverviewLabels { get; set; } = null!;
    [Export] public Vector3 GameplayCameraOffset { get; set; } = new(0, 12, 12);
    [Export] public float GameplayFov { get; set; } = 50;
    [Export] public float OverviewSize { get; set; } = 96;
    [Export] public float PanSpeed { get; set; } = 12;
    private Vector3 _focus;
    private bool _overview = true;
    private int _point;
    private Node3D[] _points = [];

    public override void _Ready()
    {
        _points = Viewpoints?.GetChildren().OfType<Node3D>().ToArray() ?? [];
        if (Camera is null || Status is null || OverviewLabels is null || _points.Length != 6)
        {
            GD.PushError("BlockoutPreview: assign camera, status and six viewpoints.");
            SetProcess(false);
            SetProcessUnhandledInput(false);
            return;
        }
        ShowOverview();
    }

    public void ShowOverview()
    {
        _overview = true;
        OverviewLabels.Visible = true;
        _focus = Vector3.Zero;
        Camera.Projection = Camera3D.ProjectionType.Orthogonal;
        Camera.Size = OverviewSize;
        Camera.RotationDegrees = new Vector3(-90, 0, 0);
        Camera.Position = new Vector3(0, 90, 0);
        UpdateStatus("Общий план");
    }

    public void ShowPoint(int index)
    {
        _point = Math.Clamp(index, 0, _points.Length - 1);
        _overview = false;
        OverviewLabels.Visible = false;
        _focus = _points[_point].Position;
        Camera.Projection = Camera3D.ProjectionType.Perspective;
        Camera.Fov = GameplayFov;
        Camera.Position = _focus + GameplayCameraOffset;
        Camera.LookAt(_focus, Vector3.Up);
        UpdateStatus($"Точка {_point + 1}");
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key6)
            ShowPoint((int)key.PhysicalKeycode - (int)Key.Key1);
        else if (key.PhysicalKeycode == Key.Tab)
        {
            if (_overview) ShowPoint(_point);
            else ShowOverview();
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_overview) return;
        // Inspection pans without rotating; input does not issue gameplay commands.
        var direction = new Vector3(
            (Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0), 0,
            (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0));
        _focus += direction.Normalized() * PanSpeed * (float)delta;
        _focus = new Vector3(Math.Clamp(_focus.X, -39, 39), 0, Math.Clamp(_focus.Z, -31, 31));
        Camera.Position = _focus + GameplayCameraOffset;
    }

    private void UpdateStatus(string view) => Status.Text =
        $"ОКРАИНА РЕЧНОЙ ПРИСТАНИ · BLOCKOUT\n{view} · 1–6: точки осмотра · Tab: план / игровая камера\nСтрелки: сдвиг камеры · Offline, без движения персонажа и серверной навигации";
}
