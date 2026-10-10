using System.Diagnostics;
using Godot;

namespace ProjectG.Regions.Export;

/// <summary>Editor and old F6 entry points use the same validated publication command as CI.</summary>
public static class RegionExportRunner
{
    public static async Task<int> Run()
    {
        var repository = ProjectSettings.GlobalizePath("res://..");
        var start = new ProcessStartInfo("powershell")
        {
            WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(repository, "tools/Export-Regions.ps1"), "-GodotPath", OS.GetExecutablePath() })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start region exporter.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        GD.Print(await output);
        var diagnostic = await error;
        if (diagnostic.Length != 0) GD.PrintErr(diagnostic);
        return process.ExitCode;
    }
}
