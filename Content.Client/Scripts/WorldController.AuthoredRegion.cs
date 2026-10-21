using Content.Shared.Navigation;
using Godot;
using ProjectG.Regions.Authoring;

namespace ProjectG.Gameplay;

public partial class WorldController
{
    private RegionRoot? _authoredRegion;
    private Node3D? _surfaceRegion;
    private NavigationGrid? _authoredBase;
    private int[] _authoredOpening = [];
    private bool _contentReady = true;

    private void LoadAuthoredRegion(string regionId, ulong expectedHash)
    {
        // Removed flat test maps must fail visibly instead of loading a missing scene.
        RejectRegion($"Регион {regionId} отсутствует в текущем клиенте. Обновите экспорт и перезапустите сервер.");
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
        // Stop native camera polling before the owning player/camera leaves.
        if (_surfaceTerrain is not null)
            _surfaceTerrain.ProcessMode = ProcessModeEnum.Disabled;
        _surfaceTerrain = null;
        DetachPresentation(_surfaceRegion); _surfaceRegion = null;
        DetachPresentation(_authoredRegion); _authoredRegion = null; _authoredBase = null;
        GetNodeOrNull<Node3D>("Ground")?.Show();
        if (GetNodeOrNull<CollisionShape3D>("Ground/CollisionShape3D") is { } fallbackGround)
            fallbackGround.Disabled = false;
        GetNode<DirectionalLight3D>("DirectionalLight3D").Show();
    }
}
