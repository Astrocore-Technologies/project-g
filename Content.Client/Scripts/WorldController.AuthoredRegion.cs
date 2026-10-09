using Content.Shared.Navigation;
using Godot;
using ProjectG.Regions.Authoring;

namespace ProjectG.Gameplay;

public partial class WorldController
{
    private RegionRoot? _authoredRegion;
    private NavigationGrid? _authoredBase;
    private int[] _authoredOpening = [];
    private bool _contentReady = true;

    private void LoadAuthoredRegion(string regionId, ulong expectedHash)
    {
        _contentReady = false;
        RegionRoot? scene = null;
        try
        {
            var scenePath = regionId switch
            {
                "prototype" => "res://Scenes/Regions/RiverLanding/RiverLandingBlockout.tscn",
                "river_city" => "res://Scenes/Regions/RiverCity/RiverCity.tscn",
                _ => throw new InvalidDataException("Неизвестная клиентская сцена региона.")
            };
            scene = GD.Load<PackedScene>(scenePath).Instantiate<RegionRoot>();
            var geometry = FlatGeometryBake.Bake(scene);
            if (geometry.Hash != expectedHash) throw new InvalidDataException("Клиентская сцена и серверная карта различаются. Пересобери экспорт и перезапусти сервер.");
            _authoredBase = geometry.CreateGrid();
            _authoredOpening = geometry.OpeningCells;
            // Inspector owns a camera/input/UI: remove it BEFORE entering the live tree.
            foreach (var path in new[] { "Preview", "AuthoringAnchors", "Decorations/ScaleReferences" })
            {
                var node = scene.GetNodeOrNull(path);
                if (node is not null) { node.GetParent().RemoveChild(node); node.Free(); }
            }
            _authoredRegion = scene;
            AddChild(scene);
            GetNode<Node3D>("Ground").Hide();
            GetNode<CollisionShape3D>("Ground/CollisionShape3D").Disabled = true;
            GetNode<DirectionalLight3D>("DirectionalLight3D").Hide();
            _contentReady = true;
        }
        catch (Exception error)
        {
            if (scene is not null && scene.GetParent() is null) scene.Free();
            RejectRegion(error.Message);
        }
    }

    private void RejectRegion(string message)
    {
        _contentReady = false;
        GD.PushError(message);
        var canvas = new CanvasLayer { Layer = 100 };
        AddChild(canvas);
        canvas.AddChild(new Label { Text = message, Position = new(24, 24) });
        _network.RejectRegionContent();
    }

    private void ApplyAuthoredNavigation(NavigationGrid grid)
    {
        if (_authoredRegion is null || _authoredBase is null) return;
        var original = _authoredBase.ToMessage();
        var received = grid.ToMessage();
        if (received.Origin != original.Origin || received.Width != original.Width || received.Height != original.Height ||
            received.CellSize != original.CellSize || received.AgentRadius != original.AgentRadius)
        { RejectRegion("Геометрия региона несовместима с клиентом."); return; }
        // Only the authored repair barrier may change; all other cells must match exactly.
        var opened = _authoredOpening.Length > 0 && received.BlockedCells[_authoredOpening[0]] == 0;
        for (var i = 0; i < original.BlockedCells.Length; i++)
        {
            var patch = Array.IndexOf(_authoredOpening, i) >= 0;
            var expected = patch && opened ? 0 : original.BlockedCells[i];
            if (received.BlockedCells[i] != expected) { RejectRegion("Сервер прислал неизвестное изменение геометрии."); return; }
        }
        if (_authoredOpening.Length == 0) return;
        var barrier = _authoredRegion.GetNode<StaticBody3D>("PublicStateBindings/Bridge/RepairBarrier");
        barrier.Visible = !opened;
        barrier.GetNode<CollisionShape3D>("Collision").Disabled = opened;
    }

    private void UnloadAuthoredRegion()
    {
        DetachPresentation(_authoredRegion); _authoredRegion = null; _authoredBase = null;
        GetNode<Node3D>("Ground").Show();
        GetNode<CollisionShape3D>("Ground/CollisionShape3D").Disabled = false;
        GetNode<DirectionalLight3D>("DirectionalLight3D").Show();
    }
}
