using System.Text.Json;
using Content.Shared.Navigation;
using Godot;
using ProjectG.Regions.Authoring;

namespace ProjectG.Tools.Regions;

/// <summary>Re-export after editing the scene in Godot, without rebuilding or overwriting it.</summary>
public partial class ExportRiverCity : Node
{
    public override async void _Ready()
    {
        try
        {
            using var root=GD.Load<PackedScene>("res://Scenes/Regions/RiverCity/RiverCity.tscn").Instantiate<RegionRoot>();
            var geometry=Export(root);
            root.Free(); // Dispose alone does not recursively free an unattached scene's native nodes.
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print($"RIVER_CITY_EXPORTED: {geometry.Hash:X16}"); GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    public static FlatRegionGeometry Export(RegionRoot root)
    {
        var geometry=FlatGeometryBake.Bake(root);
        var placements=root.GetNode("AuthoringAnchors").GetChildren().OfType<RegionMarker>()
            .ToDictionary(n=>n.Name.ToString(),n=>new[]{n.Position.X,n.Position.Z});
        var grid=geometry.CreateGrid(); var pathfinder=new NavigationPathfinder(grid); var path=new List<System.Numerics.Vector2>();
        foreach(var name in new[]{"Spawn","Guide","MagicShop","Arena","SwordTarget","SwordDummyWest","SwordDummyEast","SwordTrainer","SouthGate","SouthArrival","RiverGate","RiverArrival"})
        {
            if(!placements.TryGetValue(name,out var point)||!placements.TryGetValue("Spawn",out var spawn) ||
                !pathfinder.TryFindPath(new(spawn[0],spawn[1]),new(point[0],point[1]),path))
                throw new InvalidDataException($"Missing or unreachable city placement: {name}");
        }
        var data=ProjectSettings.GlobalizePath("res://../Content.Server/Data/Regions");
        var json=new JsonSerializerOptions{WriteIndented=true};
        File.WriteAllText(Path.Combine(data,"river-city.json"),JsonSerializer.Serialize(geometry,json));
        File.WriteAllText(Path.Combine(data,"river-city-placements.json"),JsonSerializer.Serialize(placements,json));
        return geometry;
    }
}
