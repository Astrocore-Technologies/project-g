using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.Quests;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

/// <summary>Real client/server camp flow. Test credentials and storage are isolated by the launcher.</summary>
public partial class CampActivitiesSmoke:Node
{
    private NetworkClient _network=null!;private PlayerController _player=null!;private WorldController _world=null!;
    private ActivityState _activity;private CraftState _materials;private ProgressionState _progression;
    public override async void _Ready()
    {
        try
        {
            var port=int.Parse(OS.GetCmdlineUserArgs().Single(a=>a.StartsWith("--server-port="))[14..]);
            GetWindow().Mode=Window.ModeEnum.Windowed;GetWindow().Size=new(1280,720);
            _world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();_world.AutoConnect=false;
            _world.UiSettingsPath="user://camp-smoke-"+Guid.NewGuid().ToString("N")+".cfg";
            _network=_world.GetNode<NetworkClient>("NetworkClient");_network.DevelopmentIdentityProfile="ui-c5c-"+Guid.NewGuid().ToString("N")[..10];
            _network.ActivitiesReceived+=s=>_activity=s;_network.CraftStateReceived+=s=>_materials=s;_network.ProgressionReceived+=s=>_progression=s;
            AddChild(_world);_network.ConnectToServerAt(port);
            await Wait(()=>_activity.Giver.IsValid&&_network.RegionActive&&_network.CraftRecipes.Count>0);await Frames(12);
            _player=_world.GetChildren().OfType<PlayerController>().Single(p=>p.EntityId==_activity.OwnerId);
            if(_network.ResourceNodes.Values.Any(r=>r.Height<10)||_network.CraftRecipes.Values.Any(r=>r.StationHeight<10))throw new Exception("Camp objects lost authored height.");
            await OpenNpc(_activity.Giver);var quest=_world.GetChildren().OfType<QuestPresentation>().Single();
            var dialogue=quest.FindChildren("DialogueView","",true,false).OfType<Control>().Single();
            // The original one-time delivery remains a separate option and supplies its original three wood.
            await Click(dialogue.GetNode<Button>("%Accept"));await Wait(()=>_materials.Materials?.Any(m=>m.Id==1&&m.Quantity==3)==true);
            var view=dialogue.GetNode<ActivityOrderView>("%DialogueActivities");
            await Click(view.GetNode<Button>("Actions/Accept"));await Wait(()=>_activity.Order==1&&_activity.Status==QuestStatus.Active);
            if(_activity.Carried!=3)throw new Exception("Repeatable order granted free cargo.");await Capture("camp-order-accepted-1280");
            GD.Print("C5C_STAGE recipient");await Escape();await OpenNpc(_activity.Recipient);
            await Click(view.GetNode<Button>("Actions/Deliver"));await Wait(()=>_activity.Status==QuestStatus.Completed&&_progression.Experience==20);
            if(_activity.Carried!=0)throw new Exception("Delivery did not consume cargo.");await Capture("camp-order-completed-1280");
            GD.Print("C5C_STAGE second-order");await Escape();await OpenNpc(_activity.Giver);await Click(view.GetNode<Button>("Actions/Accept"));await Wait(()=>_activity.Order==2&&_activity.Status==QuestStatus.Active);
            if(_activity.Carried!=0)throw new Exception("Next order refilled cargo.");
            GetWindow().Size=new(1920,1080);await Frames(5);await Capture("camp-next-order-1920");await Escape();
            GD.Print("C5C_STAGE gather");var ore=_network.ResourceNodes.Values.Single(r=>r.MaterialId==2);await Move(ore.Position,ore.Height);
            await PressKey(Key.C);var craft=_world.FindChildren("CraftingView","",true,false).OfType<Control>().Single();
            var gather=craft.GetNode<VBoxContainer>("%РесурсыRows").FindChildren("*","Button",true,false).OfType<Button>().Where(b=>b.Text=="Собрать").ToArray();
            await Click(gather[1]);await Wait(()=>_materials.Materials?.Any(m=>m.Id==2&&m.Quantity>=1)==true);await Escape();
            GD.Print("C5C_STAGE trade");var station=_network.CraftRecipes.Values.First();await Move(station.StationPosition,station.StationHeight);await PressKey(Key.J);
            var economy=_world.FindChildren("EconomyView","",true,false).OfType<Control>().Single();
            economy.GetNode<TabContainer>("%Workspace").CurrentTab=2;await Frames(3);
            await Click(economy.GetNode<Button>("%SellOre"));await Wait(()=>economy.GetNode<Control>("%Confirmation").Visible);
            if(!economy.GetNode<Label>("%QuoteDetails").Text.Contains("EXP персонажа: +1"))throw new Exception("Trade quote did not disclose EXP.");
            await Capture("camp-ore-quote-1920");await Click(economy.GetNode<Button>("%Confirm"));await Wait(()=>_progression.Experience==21);
            await Escape();await PressKey(Key.L);await Frames(4);await Capture("camp-journal-1920");
            GetWindow().Size=new(1280,720);await Frames(5);
            var journal=_world.FindChildren("JournalView","",true,false).OfType<Control>().Single();
            if(journal.GetNode<ScrollContainer>("Columns/Detail/Scroll").Size.Y<80)throw new Exception("Activity summary crowded out the quest description.");
            await Capture("camp-journal-1280");await Escape();GetWindow().Size=new(1920,1080);await Frames(5);
            GD.Print("C5C_STAGE battle");await Move(new(-421,327),18.72f);await Frames(8);
            var camera=GetViewport().GetCamera3D();var aim=camera.UnprojectPosition(new(-420,19.5f,327));
            Input.ParseInputEvent(new InputEventMouseMotion {Position=aim,GlobalPosition=aim});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=aim,GlobalPosition=aim});await Frames(2);
            Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=aim,GlobalPosition=aim});
            await Wait(()=>_activity.WatchPhase==CampWatchPhase.Completed&&_progression.Experience==61);
            await PressKey(Key.L);await Frames(3);await Capture("camp-watch-completed-1920");await Escape();
            if(GameUi.GameplayModalOpen)throw new Exception("Escape left activity modal input captured.");
            GD.Print("CAMP_ACTIVITIES_SMOKE_OK: real NPC dialogue, original quest, numbered order, delivery, no free repeat cargo, height, gather, quoted ore EXP, Live-DM victory bonus, journal, 1280/1920, Escape.");
            _world.QueueFree();await Frames(3);GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private async Task OpenNpc(NetworkEntityId id)
    {GD.Print("C5C_NPC "+id);var npc=_network.QuestNpcs[id];await Move(npc.Position,npc.Height);await Frames(12);await PressKey(Key.F2);await Wait(()=>_world.FindChildren("DialogueView","",true,false).OfType<Control>().Any(v=>v.IsVisibleInTree()));await Frames(8);}
    public override void _Notification(int what){if(what==NotificationWMCloseRequest)GD.Print("C5C_WINDOW_CLOSE_REQUEST");}
    private async Task Move(System.Numerics.Vector2 target,float height)
    {if(!_player.MoveTo(target,height))throw new Exception("No camp route.");await Wait(()=>System.Numerics.Vector2.Distance(_player.PredictedPosition,target)<.13f);await Frames(12);}
    private async Task Wait(Func<bool> done)
    {var end=Time.GetTicksMsec()+25000;while(!done()&&Time.GetTicksMsec()<end)await Frames(1);if(!done())throw new TimeoutException("Camp smoke deadline.");}
    private async Task Frames(int n){for(var i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task PressKey(Key key)
    {Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=true});await Frames(2);Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=false});await Frames(3);}
    private Task Escape()=>PressKey(Godot.Key.Escape);
    private async Task Click(Button button)
    {
        if(!button.IsVisibleInTree()||button.Disabled)throw new Exception("Unavailable smoke button: "+button.Name);
        var p=button.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseMotion {Position=p,GlobalPosition=p});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=p,GlobalPosition=p});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=p,GlobalPosition=p});await Frames(3);
    }
    private async Task Capture(string name)
    {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);var folder=OS.GetCmdlineUserArgs().Single(a=>a.StartsWith("--capture-dir="))[14..];GetViewport().GetTexture().GetImage().SavePng($"{folder}/{name}.png");}
}
