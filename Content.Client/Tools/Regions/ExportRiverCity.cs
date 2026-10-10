using Godot;
using ProjectG.Regions.Export;

namespace ProjectG.Tools.Regions;

// F6 publishes the complete validated set through the common pipeline.
public partial class ExportRiverCity : Node
{
    public override async void _Ready()
    {
        try { GetTree().Quit(await RegionExportRunner.Run()); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
