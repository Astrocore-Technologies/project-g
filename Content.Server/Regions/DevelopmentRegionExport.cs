using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Content.Server.Regions;

/// <summary>Refreshes authored maps during local Development startup, before opening a world or database.</summary>
public static class DevelopmentRegionExport
{
    public const string PackagePath = "Data/Regions/region-package.json";

    public static async Task EnsureCurrentAsync(IConfiguration configuration, string applicationDirectory,
        Func<string, Task> export, TextWriter log)
    {
        // Explicit package overrides and deployed builds keep their existing read-only loading path.
        if (!configuration.GetValue("RegionExports:AutoExport", true) ||
            configuration["RegionExports:PackagePath"] is not { } configured ||
            !SamePath(Path.Combine(applicationDirectory, configured), Path.Combine(applicationDirectory, PackagePath)) ||
            RegionExportCatalog.FindSourceRoot(applicationDirectory) is not { } repository)
            return;

        var published = Path.Combine(repository, "Content.Server", PackagePath);
        var needsExport = false;
        try { _ = new RegionExportCatalog(published, repository); if (File.Exists(Path.Combine(repository,"Content.Client/Scenes/Regions/TerrainTest/TerrainTest.tscn"))) _ = SurfaceCatalog.Load(Path.Combine(repository,"Content.Server",SurfaceCatalog.PackagePath),repository); }
        catch (Exception error) when (error is InvalidDataException or JsonException or FileNotFoundException)
        {
            needsExport = true;
        }
        if (needsExport)
        {
            await log.WriteLineAsync("Region data changed; exporting maps automatically.");
            await export(repository);
            // A successful process exit alone does not establish that the newly published data is current.
            _ = new RegionExportCatalog(published, repository); if (File.Exists(Path.Combine(repository,"Content.Client/Scenes/Regions/TerrainTest/TerrainTest.tscn"))) _ = SurfaceCatalog.Load(Path.Combine(repository,"Content.Server",SurfaceCatalog.PackagePath),repository);
            await log.WriteLineAsync("Region export ready. Continuing server startup.");
        }

        // dotnet run may have copied the previous package before this export. Load the published source
        // directly instead of rebuilding/replacing assemblies of the already running server.
        configuration["RegionExports:PackagePath"] = published;
        configuration["RegionExports:SurfacePackagePath"] = Path.Combine(repository,"Content.Server",SurfaceCatalog.PackagePath);
    }

    public static async Task RunExporterAsync(string repository)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(repository, "tools", "Export-Regions.ps1") })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the automatic region export.");
        // Forward both streams concurrently: IDE/launcher servers may not have an attached console,
        // and a full redirected pipe must not stall the exporter while the parent waits for exit.
        await Task.WhenAll(ForwardAsync(process.StandardOutput, Console.Out), ForwardAsync(process.StandardError, Console.Error),
            process.WaitForExitAsync());
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Automatic region export failed (exit {process.ExitCode}). Fix the export errors above before starting the server; the previous package was preserved.");
    }

    private static async Task ForwardAsync(StreamReader source, TextWriter destination)
    {
        while (await source.ReadLineAsync() is { } line) await destination.WriteLineAsync(line);
    }

    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
