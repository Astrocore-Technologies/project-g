using Godot;

namespace ProjectG.Gameplay;

public partial class WorldController
{
    private Node3D? _regionExit;
    private readonly List<Label3D> _gateLabels = [];

    private static void DetachPresentation(Node? node)
    {
        if (node is null || !GodotObject.IsInstanceValid(node)) return;
        // QueueFree alone leaves network subscriptions alive until the frame ends.
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    private void OnRegionChanged()
    {
        ClearWorld(); _gateLabels.Clear();
        if (_network.CurrentRegion is not { } region) return;
        _contentReady = true;
        if (region.HasSurface) _contentReady = false;
        else if (region.GeometryHash != 0)
        {
            LoadAuthoredRegion(region.Region, region.GeometryHash);
            if (!_contentReady) return;
        }
        _regionExit = new Node3D();
        AddChild(_regionExit);
        AddGate(new(region.Exit,region.Radius,region.ExitHeight));
        foreach (var gate in region.AdditionalGates ?? []) AddGate(gate);

        void AddGate(Content.Shared.Network.RegionGate gate)
        {
            var marker = new Node3D { Position = new(gate.Exit.X,gate.Height+.08f,gate.Exit.Y) }; _regionExit.AddChild(marker);
            marker.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = gate.Radius, BottomRadius = gate.Radius, Height = .03f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.4f,.7f,.8f,.32f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha }
            });
            var label = new Label3D
            {
                Position = new(0, 2.6f, 0), Text = "Выход в другую локацию",
                FontSize = 24, PixelSize = .006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
            };
            marker.AddChild(label); _gateLabels.Add(label);
        }
    }
}
