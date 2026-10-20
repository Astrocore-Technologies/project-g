using System.Reflection;
using Content.Shared.Network;
using Godot;
using ProjectG.Echoes;
using ProjectG.Networking;
using ProjectG.UI;

namespace ProjectG.Tests.UI;

/// <summary>Offline adapter: the real Echo controller and EchoView scene, with fixture owner state.</summary>
public partial class EchoExperiencePreview:Node
{
    private static IEnumerable<Node> Descendants(Node node)
    {foreach(var child in node.GetChildren()){yield return child;foreach(var next in Descendants(child))yield return next;}}
    public override async void _Ready()
    {
        var network=new NetworkClient(); // Deliberately outside the tree: no identity, socket or connection.
        try
        {
            var owner=new NetworkEntityId(1);var row=new EchoRosterEntry(1,"Мира — спутница",EchoAvailability.Ready,0,default,110,140,100,
                Experience:6,ExperienceFraction:2d/3);
            var roster=new EchoRosterPage(owner,1,1,0,1,[row]);
            typeof(NetworkClient).GetProperty(nameof(NetworkClient.LatestEchoRoster))!.SetValue(network,roster);
            var controls=new EchoControls();AddChild(controls);controls.Initialize(network,owner);
            var window=Descendants(controls).OfType<UiWindow>().Single();window.Open();
            var view=Descendants(controls).OfType<Control>().Single(c=>c.Name=="EchoView");var details=view.GetNode<Label>("%Details");
            var folder=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--capture-dir="))?[14..];
            foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
            {
                GetWindow().Size=size;
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(!details.Text.Contains("EXP 6") || !details.IsVisibleInTree())throw new Exception("Owner Echo EXP is not displayed by the working scene.");
                if(folder is not null)GetViewport().GetTexture().GetImage().SavePng($"{folder}/echo-experience-{size.X}.png");
            }
            Input.ParseInputEvent(new InputEventKey {Keycode=Key.Escape,Pressed=true});
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(window.Visible)throw new Exception("Escape did not close the working Echo window.");
            GD.Print("ECHO_EXPERIENCE_PREVIEW_OK: real controller/scene, fractional EXP, 1280/1920, Escape; no transport.");GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
        finally{network.Free();}
    }
}
