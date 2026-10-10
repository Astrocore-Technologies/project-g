using Godot;
using ProjectG.Regions.Export;

namespace ProjectG.Tests.Regions;

// Legacy F6 shortcut uses the same complete publication pipeline.
public partial class ExportRiverLanding : Node
{
    public override async void _Ready()
    {
        try { GetTree().Quit(await RegionExportRunner.Run()); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
