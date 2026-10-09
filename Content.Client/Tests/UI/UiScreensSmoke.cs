using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
using ProjectG.StarterZone;
namespace ProjectG.Tests.UI;

/// <summary>All live screens against an isolated server, with real input and no fixture game state.</summary>
public partial class UiScreensSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed; GetWindow().Size=new(1280,720); await Frames(3);
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false; AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient"); ExplorationState? exploration=null;
            network.ExplorationReceived+=value=>exploration=value; network.ConnectToServer();
            await Wait(()=>network.LatestQuestJournal is not null && exploration is not null); await Frames(30);
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            foreach(var control in Descendants(world).OfType<UiIconSlot>().Where(c=>c.IsVisibleInTree()))
                if(!GetViewport().GetVisibleRect().Encloses(control.GetGlobalRect())) throw new Exception("HUD icon is clipped.");
            await Capture("hud-1280");
            string[] routes=["character","inventory","map","journal","echo","guild","profession","craft","market","trade","party","clan","pvp","keeper","guide"];
            foreach(var route in routes)
            {
                if(!GameUi.HasRoute(route)) continue;
                GameUi.Navigate(route); await Frames(8);
                var modal=Descendants(world).OfType<UiWindow>().Single(w=>w.Visible);
                if(!GameUi.GameplayModalOpen) throw new Exception("Missing gameplay lock: "+route);
                var panel=modal.GetChildren().OfType<PanelContainer>().Single();
                if(!GetViewport().GetVisibleRect().Encloses(panel.GetGlobalRect())) throw new Exception("Window overflow: "+route+" "+panel.GetGlobalRect());
                var position=player.PredictedPosition; var stamina=network.LatestDefense?.Stamina;
                Press(Key.Shift); Press(Key.Q); await Frames(4);
                if(System.Numerics.Vector2.Distance(position,player.PredictedPosition)>.15 || network.LatestDefense?.Stamina<stamina-.1) throw new Exception("Gameplay input leaked: "+route);
                if(route=="map")
                {
                    var map=Descendants(modal).OfType<StarterMap>().Single(); var g=network.Navigation!; var scale=Mathf.Min(map.Size.X/g.Width,map.Size.Y/g.Height); var offset=(map.Size-new Vector2(g.Width,g.Height)*scale)/2;
                    var p=player.PredictedPosition; var point=map.GlobalPosition+offset+new Vector2((p.X-g.Origin.X)/g.CellSize,(p.Y-g.Origin.Y)/g.CellSize)*scale;
                    await Click(point); if(map.Pin is null) throw new Exception("Map did not place a known-area pin.");
                    var before=map.Pin;
                    var hidden=Enumerable.Range(0,g.CellCount).FirstOrDefault(c=>(exploration!.Value.Cells[c/8]&(1<<(c%8)))==0,-1);
                    if(hidden>=0) { point=map.GlobalPosition+offset+new Vector2(hidden%g.Width+.5f,hidden/g.Width+.5f)*scale; await Click(point); if(map.Pin!=before) throw new Exception("Unexplored area accepted a pin."); }
                }
                await Capture(route+"-1280"); GD.Print("UI_SCREEN: "+route);
            }
            GameUi.Navigate("inventory"); await Frames(5);
            var search=Descendants(world).OfType<LineEdit>().Single(e=>e.IsVisibleInTree()); search.GrabFocus(); search.Text="zz-no-item"; search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text); await Frames(4);
            if(!Descendants(world).OfType<Label>().Any(l=>l.IsVisibleInTree() && l.Text=="Ничего не найдено")) throw new Exception("Inventory search did not filter items.");
            Press(Key.Escape); await Frames(3); if(GameUi.GameplayModalOpen) throw new Exception("Escape did not close filtered inventory.");
            Press(Key.Escape); await Frames(3); await Capture("menu-1280"); Press(Key.Escape); await Frames(3);
            GetWindow().Size=new(1920,1080); await Frames(8); await Capture("hud-1920");
            foreach(var route in routes.Take(6)) { GameUi.Navigate(route); await Frames(8); await Capture(route+"-1920"); }
            world.QueueFree(); await Frames(5);
            if(GameUi.GameplayModalOpen || GameUi.HasRoute("inventory")) throw new Exception("Region teardown leaked modal or routes.");
            GD.Print("UI_SCREENS_OK: live routes, window bounds, gameplay lock, explored map pins, search, Escape, two resolutions and teardown."); GetTree().Quit();
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private static IEnumerable<Node> Descendants(Node node) { foreach(var child in node.GetChildren()) { yield return child; foreach(var next in Descendants(child)) yield return next; } }
    private async Task Wait(Func<bool> condition) { var end=Time.GetTicksMsec()+20000; while(!condition() && Time.GetTicksMsec()<end) await Frames(1); if(!condition()) throw new TimeoutException("UI screen login"); }
    private async Task Frames(int n) { for(var i=0;i<n;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    private static void Press(Key key) { Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=key,Keycode=key,Pressed=true }); Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=key,Keycode=key,Pressed=false }); }
    private async Task Click(Vector2 point) { Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Position=point,GlobalPosition=point,Pressed=true }); await Frames(1); Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Position=point,GlobalPosition=point,Pressed=false }); await Frames(3); }
    private async Task Capture(string name) { if(!OS.GetCmdlineUserArgs().Contains("--capture")) return; await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/ui-complete/"+name+".png"); }
}
