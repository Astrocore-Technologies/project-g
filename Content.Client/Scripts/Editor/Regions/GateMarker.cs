using Godot;

namespace ProjectG.Regions.Authoring;

[Tool, GlobalClass]
public partial class GateMarker : RegionMarker
{
    // Destination and eligibility belong to server rules, not client scene metadata.
    [Export(PropertyHint.Range, "0.1,10,0.05")] public float Radius { get; set; } = 0.65f;
}
