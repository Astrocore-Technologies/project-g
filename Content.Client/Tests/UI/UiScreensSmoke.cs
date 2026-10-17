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
    [Export] public bool UseReviewServer { get; set; }
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed; GetWindow().Size=new(1280,720); await Frames(3);
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false;
            var network=world.GetNode<NetworkClient>("NetworkClient"); ExplorationState? exploration=null;
            var reviewPort=0;
            if(UseReviewServer)
            {
                using var config=System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://../.artifacts/ui-redesign/live.json"));
                reviewPort=config.RootElement.GetProperty("port").GetInt32(); network.DevelopmentIdentityProfile="ui-rework-smoke-"+Guid.NewGuid().ToString("N")[..12];
            }
            network.ExplorationReceived+=value=>exploration=value; AddChild(world);
            if(reviewPort>0) network.ConnectToServerAt(reviewPort); else network.ConnectToServer();
            await Wait(()=>network.RegionActive && network.LatestExploration is not null); exploration=network.LatestExploration; await Frames(30);
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            foreach(var control in Descendants(world).OfType<UiIconSlot>().Where(c=>c.IsVisibleInTree()))
            {
                if(!GetViewport().GetVisibleRect().Encloses(control.GetGlobalRect())) throw new Exception("HUD icon is clipped.");
            }
            var hud=world.GetChildren().OfType<PlayerHud>().Single();
            var skillIcons=Descendants(hud).OfType<TextureRect>().Where(icon=>icon.Name=="Icon").ToArray();
            if(skillIcons.Length!=9 || skillIcons.Any(icon=>Math.Min(icon.Size.X,icon.Size.Y)<40)) throw new Exception("HUD skill art is too small to read.");
            if(Descendants(world).OfType<Label>().Any(l=>l.IsVisibleInTree() && (l.Text=="События мира" || l.Text.Contains("Здесь появятся вести") || l.Text.Contains("Подойдите к NPC")))) throw new Exception("Removed HUD placeholders returned.");
            var minimap=Descendants(world).OfType<UiMinimapView>().Single();
            foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
            {
                GetWindow().Size=size; await Frames(5);
                if(!GetViewport().GetVisibleRect().Encloses(minimap.GetGlobalRect())) throw new Exception("Minimap is outside the viewport.");
                var g=network.Navigation!;
                foreach(var point in new[]{g.Origin,g.Origin+new System.Numerics.Vector2(g.Width*g.CellSize,0),g.Origin+new System.Numerics.Vector2(0,g.Height*g.CellSize),g.Origin+new System.Numerics.Vector2(g.Width*g.CellSize,g.Height*g.CellSize),player.PredictedPosition})
                {
                    minimap.Update(network,network.LatestStarterZone!.Value,exploration,point,player.Position.Y,true);
                    if(minimap.Map.ProjectPosition(point).DistanceTo(minimap.Map.Size/2)>1) throw new Exception("Minimap player marker is not centered at a region edge.");
                }
                minimap.Update(network,network.LatestStarterZone!.Value,exploration,player.PredictedPosition,player.Position.Y,true);
            }
            GetWindow().Size=new(1280,720); await Frames(5);
            var mapShortcut=Descendants(world).OfType<Button>().Single(button=>button.Name=="OpenMap");
            await Click(mapShortcut.GetGlobalTransform()*(mapShortcut.Size/2));
            if(!GameUi.GameplayModalOpen || !Descendants(world).OfType<StarterMap>().Any(map=>map.Interactive && map.IsVisibleInTree())) throw new Exception("Compact map shortcut did not open the map.");
            Press(Key.Escape); await Frames(3);
            await Capture("hud-1280");
            string[] routes=["character","inventory","map","journal","echo","guild","profession","craft","market","trade","party","clan","pvp","keeper","guide"];
            foreach(var route in routes)
            {
                if(!GameUi.HasRoute(route)) continue;
                GameUi.Navigate(route); await Frames(8);
                var modal=Descendants(world).OfType<UiWindow>().Single(w=>w.Visible);
                if(!GameUi.GameplayModalOpen) throw new Exception("Missing gameplay lock: "+route);
                var panel=Descendants(modal).OfType<PanelContainer>().Single(p=>p.Name=="Frame");
                if(!GetViewport().GetVisibleRect().Encloses(panel.GetGlobalRect())) throw new Exception("Window overflow: "+route+" "+panel.GetGlobalRect());
                if(route=="character") await Wait(()=>Descendants(modal).OfType<Label>().Single(l=>l.Name=="Summary7").Text!="—");
                var position=player.PredictedPosition; var stamina=network.LatestDefense?.Stamina;
                Press(Key.Shift); Press(Key.Q); await Frames(4);
                if(System.Numerics.Vector2.Distance(position,player.PredictedPosition)>.15 || network.LatestDefense?.Stamina<stamina-.1) throw new Exception("Gameplay input leaked: "+route);
                if(route=="map")
                {
                    var map=Descendants(modal).OfType<StarterMap>().Single(); var g=network.Navigation!;
                    var p=player.PredictedPosition; var point=map.GetGlobalTransform()*map.ProjectPosition(p);
                    await Click(point); if(map.Pin is null) throw new Exception("Map did not place a known-area pin.");
                    var before=map.Pin;
                    var hidden=Enumerable.Range(0,g.CellCount).FirstOrDefault(c=>(exploration!.Value.Cells[c/8]&(1<<(c%8)))==0 && new Rect2(Vector2.Zero,map.Size).HasPoint(map.ProjectPosition(g.Origin+new System.Numerics.Vector2(c%g.Width+.5f,c/g.Width+.5f)*g.CellSize)),-1);
                    if(hidden>=0) { point=map.GetGlobalTransform()*map.ProjectPosition(g.Origin+new System.Numerics.Vector2(hidden%g.Width+.5f,hidden/g.Width+.5f)*g.CellSize); await Click(point); if(map.Pin!=before) throw new Exception("Unexplored area accepted a pin."); }
                }
                await Capture(route+"-1280"); GD.Print("UI_SCREEN: "+route);
            }
            GameUi.Navigate("inventory"); await Frames(5);
            var search=Descendants(world).OfType<LineEdit>().Single(e=>e.IsVisibleInTree()); search.GrabFocus(); search.Text="zz-no-item"; search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text); await Frames(4);
            if(!Descendants(world).OfType<Label>().Any(l=>l.IsVisibleInTree() && l.Text=="Ничего не найдено")) throw new Exception("Inventory search did not filter items.");
            Press(Key.Escape); await Frames(3); if(GameUi.GameplayModalOpen) throw new Exception("Escape did not close filtered inventory.");
            Press(Key.Escape); await Frames(3); await Capture("menu-1280");
            var more=Descendants(world).OfType<Button>().Single(b=>b.Name=="ToggleMore"); await Click(more.GetGlobalRect().GetCenter()); await Frames(3);
            var menu=Descendants(world).OfType<UiWindow>().Single(w=>w.Visible);
            foreach(var button in Descendants(menu).OfType<Button>().Where(b=>b.IsVisibleInTree()))
                if(!GetViewport().GetVisibleRect().Encloses(button.GetGlobalRect())) throw new Exception("Expanded menu control is clipped: "+button.Name);
            await Capture("menu-more-1280"); Press(Key.Escape); await Frames(3);
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
    private async Task Capture(string name) { if(!UseReviewServer && !OS.GetCmdlineUserArgs().Contains("--capture")) return; DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://../.artifacts/ui-complete")); await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/ui-complete/"+name+".png"); }
}
