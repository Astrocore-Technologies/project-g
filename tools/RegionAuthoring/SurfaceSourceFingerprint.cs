using System.Text;
using System.Text.RegularExpressions;
using Content.Shared.Navigation;

namespace ProjectG.Authoring;

/// <summary>Editor and headless server agree on external authored terrain inputs without loading Godot.</summary>
public static partial class SurfaceSourceFingerprint
{
    public static string Compute(string clientRoot, string scenePath)
    {
        if (!scenePath.StartsWith("res://Scenes/Regions/", StringComparison.Ordinal) || scenePath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid authored surface path.");
        var text = File.ReadAllText(Path.Combine(clientRoot, scenePath[6..]));
        return ComputeInputs(text,
            directory=>Directory.GetFiles(Path.Combine(clientRoot,directory[6..]),"*.res").Select(file=>Path.GetFileName(file)),
            (directory,file)=>
            {
                var path=Path.Combine(clientRoot,directory[6..],file);
                if(new FileInfo(path).Length>16*1024*1024) throw new InvalidDataException("Authored terrain region exceeds budget.");
                return File.ReadAllBytes(path);
            });
    }

    // Godot supplies FileAccess/DirAccess readers for exported PCKs; the server
    // supplies ordinary filesystem readers. The fingerprint is identical.
    public static string ComputeInputs(string text,Func<string,IEnumerable<string>> listFiles,Func<string,string,byte[]> readBytes)
    {
        var match = DirectoryDeclaration().Match(text);
        if (!match.Success) return SurfaceGeometry.Fingerprint(text);
        var directory = match.Groups[1].Value;
        if (!directory.StartsWith("res://Assets/World/", StringComparison.Ordinal) || directory.Contains("..", StringComparison.Ordinal) || directory.Contains('\\'))
            throw new InvalidDataException("Terrain source directory must stay inside client world assets.");
        var files = listFiles(directory).Where(file=>file.EndsWith(".res",StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        if (files.Length is < 1 or > 64) throw new InvalidDataException("Invalid authored terrain region count.");
        var input = new StringBuilder(text);
        long total=0;
        foreach (var file in files)
        {
            if(file!=Path.GetFileName(file) || file.Contains("..",StringComparison.Ordinal)) throw new InvalidDataException("Invalid authored terrain filename.");
            var bytes=readBytes(directory,file); total+=bytes.Length;
            if(bytes.Length>16*1024*1024 || total>64*1024*1024) throw new InvalidDataException("Authored terrain inputs exceed budget.");
            // Names and bytes matter; timestamps and machine-specific absolute paths do not.
            input.Append('\n').Append(file).Append(':').Append(SurfaceGeometry.Fingerprint(Convert.ToBase64String(bytes)));
        }
        return SurfaceGeometry.Fingerprint(input.ToString());
    }

    [GeneratedRegex("""^metadata/surface_source_directory = "([^"]+)"\r?$""", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DirectoryDeclaration();
}
