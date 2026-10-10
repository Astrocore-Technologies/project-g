using Godot;

namespace ProjectG.Regions.Export;

/// <summary>Open this script in Godot and use File > Run after saving region scenes.</summary>
[Tool]
public partial class ExportRegionsInEditor : EditorScript
{
    public override async void _Run()
    {
        GD.Print("Exporting saved region scenes; progress/result will appear here.");
        try
        {
            if (await RegionExportRunner.Run() != 0) GD.PushError("Region export failed; see Output.");
        }
        catch (Exception error) { GD.PushError(error.ToString()); }
    }
}
