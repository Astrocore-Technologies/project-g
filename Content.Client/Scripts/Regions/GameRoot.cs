using Godot;
using ProjectG.Regions.Authoring;

namespace ProjectG.Regions;

/// <summary>Persistent offline preview shell. Network activation is implemented in R4.</summary>
public partial class GameRoot : Node
{
    [Export] public Node3D RegionHost { get; set; } = null!;
    [Export] public PackedScene PrototypeScene { get; set; } = null!;
    [Export] public PackedScene OutskirtsScene { get; set; } = null!;
    [Export] public Button PrototypeButton { get; set; } = null!;
    [Export] public Button OutskirtsButton { get; set; } = null!;
    [Export] public Label StatusLabel { get; set; } = null!;
    public RegionRoot? ActiveRegion { get; private set; }
    private bool _bound;

    public override void _Ready()
    {
        if (RegionHost is null || PrototypeScene is null || OutskirtsScene is null ||
            PrototypeButton is null || OutskirtsButton is null || StatusLabel is null)
        {
            GD.PushError("GameRoot: assign all exported scene/node references.");
            return;
        }
        PrototypeButton.Pressed += ShowPrototype;
        OutskirtsButton.Pressed += ShowOutskirts;
        _bound = true;
        ShowPrototype();
    }

    public bool ShowRegion(PackedScene scene)
    {
        // Validate before replacing the current preview so a broken scene leaves it usable.
        var candidate = scene.Instantiate();
        if (candidate is not RegionRoot region)
        {
            candidate.Free();
            StatusLabel.Text = "Preview error: expected RegionRoot.";
            return false;
        }
        var errors = RegionAuthoringValidation.Validate(region);
        if (errors.Count != 0)
        {
            StatusLabel.Text = string.Join("\n", errors);
            region.Free();
            return false;
        }
        if (ActiveRegion is not null)
        {
            RegionHost.RemoveChild(ActiveRegion);
            ActiveRegion.QueueFree();
        }
        ActiveRegion = region;
        RegionHost.AddChild(region);
        StatusLabel.Text = $"OFFLINE PREVIEW · {region.RegionId}\nEdit scene → save → restart preview. No server connection.";
        return true;
    }

    private void ShowPrototype() => ShowRegion(PrototypeScene);
    private void ShowOutskirts() => ShowRegion(OutskirtsScene);

    public override void _ExitTree()
    {
        if (_bound)
        {
            PrototypeButton.Pressed -= ShowPrototype;
            OutskirtsButton.Pressed -= ShowOutskirts;
            _bound = false;
        }
        ActiveRegion = null; // RegionHost owns and frees its remaining subtree.
    }
}
