using System.Text.Json;
using Godot;
using ProjectG.Regions.Authoring;

namespace ProjectG.Tests.Regions;

public partial class ExportRiverLanding : Node
{
    public override void _Ready()
    {
        try
        {
            using var root = GD.Load<PackedScene>("res://Scenes/Regions/RiverLanding/RiverLandingBlockout.tscn").Instantiate<RegionRoot>();
            var geometry = FlatGeometryBake.Bake(root);
            var grid = geometry.CreateGrid();
            var path = ProjectSettings.GlobalizePath("res://../Content.Server/Data/Regions/river-landing.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(geometry, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"RIVER_EXPORT_OK: {geometry.Hash:X16}, {grid.CellCount} cells");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
