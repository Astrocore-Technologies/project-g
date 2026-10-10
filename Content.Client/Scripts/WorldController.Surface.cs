using Content.Shared.Navigation;
using Godot;

namespace ProjectG.Gameplay;

public partial class WorldController
{
    private void LoadSurfaceRegion(NavigationGrid grid)
    {
        if (_surfaceRegion is not null) return;
        Node3D? scene = null;
        try
        {
            var geometry = grid.Surface!.Map;
            if (SurfaceGeometry.Fingerprint(Godot.FileAccess.GetFileAsString(geometry.ScenePath)) != geometry.SourceHash)
                throw new InvalidDataException("Сцена рельефа отличается от серверной. Перезапусти сервер после изменения карты.");
            scene = GD.Load<PackedScene>(geometry.ScenePath).Instantiate<Node3D>();
            ValidatePublic(scene);
            _surfaceRegion = scene; AddChild(scene);
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
    private static void ValidatePublic(Node node)
    {
        if (node.GetScript().VariantType != Variant.Type.Nil) throw new InvalidDataException("Unexpected script in public surface scene.");
        foreach (var child in node.GetChildren()) ValidatePublic(child);
    }
}
