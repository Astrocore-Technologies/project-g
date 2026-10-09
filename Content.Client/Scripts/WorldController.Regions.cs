using Godot;

namespace ProjectG.Gameplay;

public partial class WorldController
{
    private Node3D? _regionExit;

    private static void DetachPresentation(Node? node)
    {
        if (node is null || !GodotObject.IsInstanceValid(node)) return;
        // QueueFree alone leaves network subscriptions alive until the frame ends.
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    private void OnRegionChanged()
    {
        ClearWorld();
        if (_network.CurrentRegion is not { } region) return;
        _contentReady = true;
        if (region.GeometryHash != 0)
        {
            if (region.Region != "prototype") { RejectRegion("Неизвестная клиентская сцена региона."); return; }
            LoadAuthoredRegion(region.GeometryHash);
            if (!_contentReady) return;
        }
        _regionExit = new Node3D { Position = new(region.Exit.X, .08f, region.Exit.Y) };
        AddChild(_regionExit);
        _regionExit.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = region.Radius, BottomRadius = region.Radius, Height = .12f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.15f, .65f, .95f) }
        });
        _regionExit.AddChild(new Label3D
        {
            Position = new(0, 1.6f, 0), Text = "Переход в соседний регион\nПодойди к границе",
            FontSize = 32, PixelSize = .007f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
        });
    }
}
