using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;

namespace ProjectG.Tests.UI;

/// <summary>Isolated real-server smoke: NPC interaction, saved binding, death timer and respawn.</summary>
public partial class TavernSmoke:Node
{
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            var port=int.Parse(args.Single(a=>a.StartsWith("--server-port="))[14..]);
            GetWindow().Mode=Window.ModeEnum.Windowed;GetWindow().Size=new(1280,720);await Frames(3);
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();world.AutoConnect=false;
            world.UiSettingsPath="user://tavern-smoke-"+Guid.NewGuid().ToString("N")+".cfg";
            var network=world.GetNode<NetworkClient>("NetworkClient");network.DevelopmentIdentityProfile="ui-c3-"+Guid.NewGuid().ToString("N")[..12];
            network.AbilityResultReceived+=result=>GD.Print("TAVERN_SMOKE_ABILITY: ",result);
            network.AbilityLoadoutReceived+=loadout=>GD.Print("TAVERN_SMOKE_LOADOUT: ",string.Join(",",loadout.Abilities.Select(a=>a.Id)));
            AddChild(world);network.ConnectToServerAt(port);
            await Wait(()=>network.LatestTavern is not null&&network.QuestNpcs.Values.Any(n=>n.Name=="Лесная таверна"));
            await Frames(20);
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>network.KnownPlayers[p.EntityId].PlayerId==network.LocalPlayerId);
            var tavern=world.GetChildren().OfType<ProjectG.Quests.QuestPresentation>().Single().GetChildren().OfType<TavernPresentation>().Single();
            // Exercise the same public interaction route as the F2 key, including nearest-NPC selection.
            Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=Key.F2,Keycode=Key.F2,Pressed=true});await Frames(2);
            Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=Key.F2,Keycode=Key.F2,Pressed=false});await Frames(3);
            var view=(TavernView)tavern.FindChild("TavernView",true,false);
            if(!view.IsVisibleInTree())throw new Exception("F2 did not open the nearby tavern.");
            await Capture("tavern-1280");
            await Click(view.GetNode<Button>("%Bind"));await Wait(()=>network.LatestTavern?.TavernId==102);
            if(!view.GetNode<Label>("%CurrentHome").Text.Contains("Лесная таверна"))throw new Exception("Saved home did not reach the view.");
            GetWindow().Size=new(1920,1080);await Frames(5);await Capture("tavern-1920");
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=true});await Frames(2);
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=false});await Frames(2);
            if(view.IsVisibleInTree()||GameUi.GameplayModalOpen)throw new Exception("Escape left the modal open.");
            GetWindow().Size=new(1280,720);await Frames(4);
            network.SendAbility(new(1,network.LatestServerTick,2,player.PredictedPosition,AimHeight:player.PredictedFoot.Y));
            await Wait(()=>network.KnownLife.TryGetValue(player.EntityId,out var state)&&state.Dead);
            var life=world.GetChildren().OfType<LifePresentation>().Single();await Frames(5);
            var button=life.FindChildren("*","Button",true,false).Cast<Button>().Single(b=>b.Text=="Вернуться в таверну");
            if(!button.Disabled||!button.IsVisibleInTree())throw new Exception("The death countdown did not disable the respawn button.");
            await Capture("death-1280");
            GD.Print("TAVERN_SMOKE_WAIT: binding saved; real death; waiting for the 60-second server timer.");
            await Wait(()=>!button.Disabled,70000);
            await Click(button);await Wait(()=>network.KnownLife.TryGetValue(player.EntityId,out var state)&&!state.Dead);
            if(network.LatestTavern?.TavernId!=102)throw new Exception("Respawn lost its tavern.");
            await Frames(5);await Capture("respawn-1280");
            GD.Print("TAVERN_SMOKE_OK: F2, free committed binding, 1280/1920, Escape, real death, 60s gate, tavern respawn.");
            world.QueueFree();await Frames(2);GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private async Task Wait(Func<bool> condition,int milliseconds=20000)
    {var end=Time.GetTicksMsec()+(ulong)milliseconds;while(!condition()&&Time.GetTicksMsec()<end)await Frames(1);if(!condition())throw new TimeoutException("Tavern smoke deadline.");}
    private async Task Frames(int count){for(var i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Click(Button button)
    {
        var point=button.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion{Position=point,GlobalPosition=point});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point});await Frames(2);
    }
    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var directory=OS.GetCmdlineUserArgs().Single(a=>a.StartsWith("--capture-dir="))[14..];
        GetViewport().GetTexture().GetImage().SavePng($"{directory}/{name}.png");
    }
}
