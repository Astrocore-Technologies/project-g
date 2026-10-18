using Godot;
namespace ProjectG.Tests.UI;

/// <summary>Per-run evidence survives concurrent Godot instances rotating the shared engine log.</summary>
public static class UiReviewEvidence
{
    public static void Record(string scenario,string result)
    {
        var directory=ProjectSettings.GlobalizePath("res://../.artifacts/ui-final-2026-10-18/results");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory,scenario+".json"),System.Text.Json.JsonSerializer.Serialize(new{scenario,result,at=DateTimeOffset.UtcNow,renderer=ProjectSettings.GetSetting("rendering/renderer/rendering_method").AsString()}));
    }
    public static async Task Capture(Node owner,string name)
    {
        var directory=ProjectSettings.GlobalizePath("res://../.artifacts/ui-final-2026-10-18/fixtures");System.IO.Directory.CreateDirectory(directory);
        await owner.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        owner.GetViewport().GetTexture().GetImage().SavePng(directory+"/"+name+".png");
    }
}
