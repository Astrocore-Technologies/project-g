using Godot;

namespace ProjectG.Regions.Authoring;

/// <summary>Placement identity only; markers never spawn or simulate gameplay.</summary>
[Tool]
public abstract partial class RegionMarker : Marker3D
{
    [Export] public string AuthoredObjectId { get; set; } = "";

    public override string[] _GetConfigurationWarnings()
    {
        return Guid.TryParseExact(AuthoredObjectId, "D", out var id) && id != Guid.Empty
            ? [] : ["Assign a non-empty UUID (D format). Duplicated placements need a new ID."];
    }
}
