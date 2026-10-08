using Godot;

namespace ProjectG.Regions.Authoring;

[Tool, GlobalClass]
public partial class RegionRoot : Node3D
{
    [Export] public string RegionId { get; set; } = "";
    [Export] public Rect2 MovementBounds { get; set; } = new(-15, -15, 30, 30);

    public override string[] _GetConfigurationWarnings() => RegionAuthoringValidation.Validate(this).ToArray();
}
