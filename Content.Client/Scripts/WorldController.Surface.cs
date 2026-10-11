using Content.Shared.Navigation;
using Godot;

namespace ProjectG.Gameplay;

public partial class WorldController
{
    private Node3D? _surfaceTerrain;
    private void LoadSurfaceRegion(NavigationGrid grid)
    {
        if (_surfaceRegion is not null) return;
        Node3D? scene = null;
        try
        {
            var geometry = grid.Surface!.Map;
            if (ProjectG.Authoring.SurfaceSourceFingerprint.ComputeInputs(Godot.FileAccess.GetFileAsString(geometry.ScenePath),
                directory=>DirAccess.GetFilesAt(directory),
                (directory,file)=>Godot.FileAccess.GetFileAsBytes(directory+"/"+file)) != geometry.SourceHash)
                throw new InvalidDataException("Сцена рельефа отличается от серверной. Перезапусти сервер после изменения карты.");
            scene = GD.Load<PackedScene>(geometry.ScenePath).Instantiate<Node3D>();
            ValidatePublic(scene);
            _surfaceRegion = scene; AddChild(scene);
            // Region geometry loads before the authoritative local-player baseline.
            // Terrain3D must not try to acquire a camera until that player exists.
            _surfaceTerrain=scene.GetNodeOrNull<Node3D>("Geometry/Terrain");
            _surfaceTerrain?.SetPhysicsProcess(false);
            GetNode<Node3D>("Ground").Hide(); GetNode<CollisionShape3D>("Ground/CollisionShape3D").Disabled = true;
            GetNode<DirectionalLight3D>("DirectionalLight3D").Hide();
            _contentReady = true;
        }
        catch (Exception error)
        {
            if (scene is not null && scene.GetParent() is null) scene.Free();
            RejectRegion(error.Message);
        }
    }
    private void AssignSurfaceCamera()
    {
        if(_surfaceTerrain is null || GetViewport().GetCamera3D() is not {} camera) return;
        _surfaceTerrain.Call("set_camera",camera);
        _surfaceTerrain.SetPhysicsProcess(true);
    }
    private static void ValidatePublic(Node node)
    {
        if (node.GetScript().VariantType != Variant.Type.Nil) throw new InvalidDataException("Unexpected script in public surface scene.");
        foreach (var child in node.GetChildren()) ValidatePublic(child);
    }
}
