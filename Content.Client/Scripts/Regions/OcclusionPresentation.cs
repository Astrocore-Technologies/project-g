using Godot;

namespace ProjectG.Regions;

/// <summary>Static region occluders, indexed once. Visual fading never modifies collision or entity visibility.</summary>
public partial class OcclusionPresentation : Node
{
    [Export(PropertyHint.Range, "0.02,0.2,0.01")] public double UpdateInterval { get; set; } = .05;
    [Export(PropertyHint.Range, "0,1,0.05")] public float ActorClearance { get; set; } = .35f;
    public const float OccludedOpacity = .2f;
    private const float CellSize = 16;
    private readonly Dictionary<Vector2I, List<Entry>> _cells = new();
    private readonly HashSet<Entry> _candidates = new();
    private readonly HashSet<Entry> _active = new();
    private readonly List<Entry> _restore = new();
    private OcclusionMaterials _materials = new(OccludedOpacity);
    private Node3D? _focus, _region;
    private Camera3D? _camera;
    private double _elapsed;
    private bool _enabled = true;
    public int IndexedMeshCount { get; private set; }
    public int FadedMeshCount => _active.Count;
    public int UnsupportedMaterialCount => _materials.UnsupportedCount;

    public override void _Ready() => SetProcess(_focus is not null);

    public void Initialize(Node3D focus, Node3D region)
    {
        Restore(); _cells.Clear(); IndexedMeshCount = 0;
        _focus = focus; _region = region; _camera = GetViewport().GetCamera3D();
        SetProcess(true);
        _materials = new(OccludedOpacity);
        var meshes = new HashSet<MeshInstance3D>();
        Collect(region, false);
        foreach (var mesh in meshes)
        {
            var bounds = mesh.GlobalTransform * mesh.GetAabb();
            var entry = new Entry(mesh, bounds);
            var first = Cell(bounds.Position); var last = Cell(bounds.End);
            for (var x = first.X; x <= last.X; x++)
                for (var z = first.Y; z <= last.Y; z++)
                {
                    var key = new Vector2I(x, z);
                    if (!_cells.TryGetValue(key, out var entries)) _cells.Add(key, entries = new());
                    entries.Add(entry);
                }
            IndexedMeshCount++;
        }
        Refresh();

        void Collect(Node node, bool marked)
        {
            if (node.IsInGroup("camera_occlusion_ignore")) return;
            marked |= node.IsInGroup("camera_fade") || node.IsInGroup("camera_roof") ||
                      node.IsInGroup("forest_crown") || node.IsInGroup("forest_upper_branches");
            if (marked && node is MeshInstance3D { Mesh: not null } mesh) meshes.Add(mesh);
            if (node.IsInGroup("surface_occluder"))
            {
                // Existing exported scenery already identifies the matching visual.
                // Older assets use a sibling named Visual. No scene/hash rewrite is needed.
                var visual = node.HasMeta("collision_visual")
                    ? node.GetNodeOrNull<Node3D>(node.GetMeta("collision_visual").AsNodePath())
                    : node.GetParent()?.GetNodeOrNull<Node3D>("Visual");
                if (visual is not null) CollectMarked(visual);
            }
            foreach (var child in node.GetChildren()) Collect(child, marked);
        }
        void CollectMarked(Node node)
        {
            if (node.IsInGroup("camera_occlusion_ignore")) return;
            if (node is MeshInstance3D { Mesh: not null } mesh) meshes.Add(mesh);
            foreach (var child in node.GetChildren()) CollectMarked(child);
        }
    }

    public override void _Process(double delta)
    {
        if (!_enabled) return;
        _elapsed += delta;
        if (_elapsed < UpdateInterval) return;
        _elapsed = 0;
        Refresh();
    }

    public void Refresh()
    {
        if (!_enabled) return;
        if (!IsInstanceValid(_focus) || !IsInstanceValid(_region) || !_region!.IsInsideTree()) { Restore(); return; }
        if (!IsInstanceValid(_camera) || !_camera!.Current) _camera = GetViewport().GetCamera3D();
        if (_camera is null) { Restore(); return; }
        var start = _camera.GlobalPosition;
        var foot = _focus!.GlobalPosition;
        var head = foot + Vector3.Up * 1.7f;
        var margin = Vector3.One * ActorClearance;
        var first = Cell(start.Min(foot).Min(head) - margin);
        var last = Cell(start.Max(foot).Max(head) + margin);
        _candidates.Clear();
        for (var x = first.X; x <= last.X; x++)
            for (var z = first.Y; z <= last.Y; z++)
                if (_cells.TryGetValue(new(x, z), out var entries))
                    foreach (var entry in entries) _candidates.Add(entry);
        _restore.Clear();
        foreach (var entry in _active)
            if (!_candidates.Contains(entry)) _restore.Add(entry);
        foreach (var entry in _candidates)
        {
            var bounds = entry.Bounds.Grow(ActorClearance);
            var blocked = IsInstanceValid(entry.Mesh) && entry.Mesh.IsVisibleInTree() &&
                (bounds.IntersectsSegment(start, foot) || bounds.IntersectsSegment(start, head) ||
                 bounds.IntersectsSegment(start, foot.Lerp(head, .5f)));
            if (blocked)
            {
                if (_active.Add(entry)) entry.Fade(_materials);
            }
            else if (_active.Contains(entry)) _restore.Add(entry);
        }
        foreach (var entry in _restore) { entry.Restore(); _active.Remove(entry); }
    }

    public void Restore()
    {
        foreach (var entry in _active) entry.Restore();
        _active.Clear();
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (!enabled) Restore(); else Refresh();
    }

    public override void _ExitTree()
    {
        Restore(); _cells.Clear(); _candidates.Clear(); _restore.Clear();
        _focus = null; _region = null; _camera = null;
    }

    private static Vector2I Cell(Vector3 point) => new(Mathf.FloorToInt(point.X / CellSize), Mathf.FloorToInt(point.Z / CellSize));

    private sealed class Entry(MeshInstance3D mesh, Aabb bounds)
    {
        public MeshInstance3D Mesh { get; } = mesh;
        public Aabb Bounds { get; } = bounds;
        private Material? _originalOverride;
        private Material?[] _surfaces = [];

        public void Fade(OcclusionMaterials materials)
        {
            _originalOverride = Mesh.MaterialOverride;
            if (_originalOverride is not null) Mesh.MaterialOverride = materials.Faded(_originalOverride);
            else
            {
                _surfaces = new Material?[Mesh.Mesh.GetSurfaceCount()];
                for (var i = 0; i < _surfaces.Length; i++)
                {
                    _surfaces[i] = Mesh.GetSurfaceOverrideMaterial(i);
                    Mesh.SetSurfaceOverrideMaterial(i, materials.Faded(Mesh.GetActiveMaterial(i)));
                }
            }
        }

        public void Restore()
        {
            if (!IsInstanceValid(Mesh)) return;
            Mesh.MaterialOverride = _originalOverride;
            for (var i = 0; i < _surfaces.Length; i++) Mesh.SetSurfaceOverrideMaterial(i, _surfaces[i]);
            _surfaces = [];
        }
    }
}
