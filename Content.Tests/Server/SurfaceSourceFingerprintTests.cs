using Content.Shared.Navigation;
using ProjectG.Authoring;
using Xunit;

namespace Content.Tests.Server;

public sealed class SurfaceSourceFingerprintTests
{
    [Fact]
    public void LegacyScenesKeepTheSameFingerprint()
    {
        WithClient(root =>
        {
            var scene="[gd_scene format=3]\n[node name=\"Test\" type=\"Node3D\"]\n";
            File.WriteAllText(Path.Combine(root,"Scenes/Regions/Test.tscn"),scene);
            Assert.Equal(SurfaceGeometry.Fingerprint(scene),SurfaceSourceFingerprint.Compute(root,"res://Scenes/Regions/Test.tscn"));
        });
    }

    [Fact]
    public void SculptedTileBytesAndTileMembershipInvalidateTheExport()
    {
        WithClient(root =>
        {
            Directory.CreateDirectory(Path.Combine(root,"Assets/World/Terrain/Data"));
            File.WriteAllText(Path.Combine(root,"Scenes/Regions/Test.tscn"),"metadata/surface_source_directory = \"res://Assets/World/Terrain/Data\"\n");
            var tile=Path.Combine(root,"Assets/World/Terrain/Data/terrain3d_00_00.res");
            File.WriteAllBytes(tile,[1,2,3]);
            var original=SurfaceSourceFingerprint.Compute(root,"res://Scenes/Regions/Test.tscn");
            var scene=File.ReadAllText(Path.Combine(root,"Scenes/Regions/Test.tscn"));
            Assert.Equal(original,SurfaceSourceFingerprint.ComputeInputs(scene,
                _=>["terrain3d_00_00.res"],(_,_)=>[1,2,3]));
            File.WriteAllBytes(tile,[1,2,4]);
            var sculpted=SurfaceSourceFingerprint.Compute(root,"res://Scenes/Regions/Test.tscn");
            Assert.NotEqual(original,sculpted);
            File.WriteAllBytes(Path.Combine(root,"Assets/World/Terrain/Data/terrain3d_00_01.res"),[1,2,3]);
            Assert.NotEqual(sculpted,SurfaceSourceFingerprint.Compute(root,"res://Scenes/Regions/Test.tscn"));
        });
    }

    [Fact]
    public void SourceDirectoryCannotEscapeTheWorldAssets()
    {
        WithClient(root =>
        {
            File.WriteAllText(Path.Combine(root,"Scenes/Regions/Test.tscn"),"metadata/surface_source_directory = \"res://Assets/World/../../private\"\n");
            Assert.Throws<InvalidDataException>(()=>SurfaceSourceFingerprint.Compute(root,"res://Scenes/Regions/Test.tscn"));
        });
    }

    private static void WithClient(Action<string> test)
    {
        var root=Path.Combine(Path.GetTempPath(),"project-g-surface-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"Scenes/Regions"));
        try { test(root); }
        finally { Directory.Delete(root,true); }
    }
}
