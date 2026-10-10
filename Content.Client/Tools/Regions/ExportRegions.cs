using Godot;

namespace ProjectG.Regions.Export;

/// <summary>Headless entry point; publication happens only after server validation in Export-Regions.ps1.</summary>
public partial class ExportRegions : Node
{
    public override void _Ready()
    {
        try
        {
            var option = OS.GetCmdlineUserArgs().SingleOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))
                ?? throw new ArgumentException("Use tools/Export-Regions.ps1 or supply --output=<candidate>.");
            RegionPackageExporter.WriteCandidate(option[9..]);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
