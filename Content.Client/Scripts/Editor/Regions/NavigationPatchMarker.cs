using Godot;

namespace ProjectG.Regions.Authoring;

[Tool, GlobalClass]
public partial class NavigationPatchMarker : RegionMarker
{
    [Export] public Vector2 Footprint { get; set; } = Vector2.One;
}
