using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.Progression;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

/// <summary>Actual client/server UI flow on an isolated server passed via command line.</summary>
public partial class RespecSmoke:Node
{
    private ProgressionState _state;
    private RespecResult? _result;
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();var port=int.Parse(args.Single(a=>a.StartsWith("--server-port="))[14..]);
            GetWindow().Mode=Window.ModeEnum.Windowed;GetWindow().Size=new(1280,720);await Frames(3);
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();world.AutoConnect=false;
            world.UiSettingsPath="user://respec-smoke-"+Guid.NewGuid().ToString("N")+".cfg";
            var network=world.GetNode<NetworkClient>("NetworkClient");network.DevelopmentIdentityProfile="ui-c4-"+Guid.NewGuid().ToString("N")[..12];
            network.ProgressionReceived+=state=>_state=state;network.RespecResultReceived+=result=>_result=result;
            AddChild(world);network.ConnectToServerAt(port);await Wait(()=>_state.Stats is not null&&network.RegionActive&&network.QuestNpcs.Values.Any(n=>n.Name=="Лесная таверна"));await Frames(12);
            var character=world.GetChildren().OfType<ProgressionPresentation>().Single();
            var startingPoints=_state.StatPoints;var startingStats=_state.Stats.ToArray();
            await Invest(character,3); // Actual character window and authoritative preview/apply.
            var reset=await OpenReset(world);var confirm=reset.GetNode<Button>("%Confirm");
            if(!confirm.Disabled||!reset.GetNode<Label>("%Summary").Text.Contains("бесплатно"))throw new Exception("First free quote/confirmation gate missing.");
            await Capture("respec-free-1280");
            var scroll=reset.GetNode<ScrollContainer>("ComparisonScroll");var point=scroll.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion{Position=point,GlobalPosition=point});await Frames(2);
            for(var i=0;i<12;i++){Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.WheelDown,Pressed=true,Position=point,GlobalPosition=point});await Frames(1);}
            if(scroll.ScrollVertical==0)throw new Exception("Derived stat comparison does not scroll with the wheel.");
            await Capture("respec-derived-1280");await Click(reset.GetNode<Button>("%Close"));await Frames(3);
            if(_state.StatPoints!=startingPoints-3)throw new Exception("Cancel mutated the build.");
            reset=await OpenReset(world);
            var acknowledgement=reset.GetNode<CheckBox>("%Acknowledge");
            await Wait(()=>!acknowledgement.Disabled);acknowledgement.GrabFocus();
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Space,PhysicalKeycode=Key.Space,Pressed=true});await Frames(1);
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Space,PhysicalKeycode=Key.Space,Pressed=false});await Frames(2);
            if(reset.GetNode<Button>("%Confirm").Disabled)
                throw new Exception("Confirmation remains disabled: "+reset.GetNode<Label>("%Status").Text);
            await Capture("respec-confirm-1280");
            await ActivateWithKeyboard(reset.GetNode<Button>("%Confirm"));await Wait(()=>_result?.Outcome==RespecOutcome.Accepted&&_state.StatPoints==startingPoints);
            if(_result?.PaidCoins!=0||!_state.Stats.SequenceEqual(startingStats))throw new Exception("Free reset failed.");
            await Frames(3);await Capture("respec-completed-1280");
            await ActivateWithKeyboard(reset.GetNode<Button>("%Allocate"));await Frames(3);
            if(!character.FindChildren("*","TabContainer",true,false).OfType<TabContainer>().Single().IsVisibleInTree())throw new Exception("Allocate route did not open the character.");
            character.Toggle();await Frames(3);await Invest(character,2);
            reset=await OpenReset(world);GetWindow().Size=new(1920,1080);await Frames(5);
            if(!reset.GetNode<Label>("%Summary").Text.Contains("100 монет")||!reset.GetNode<Button>("%Confirm").Disabled)
                throw new Exception("Paid quote or insufficient-coins gate missing.");
            await Capture("respec-paid-1920");
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=true});await Frames(2);
            Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=false});await Frames(2);
            if(reset.IsVisibleInTree()||GameUi.GameplayModalOpen)throw new Exception("Escape left the modal open.");
            GD.Print("RESPEC_SMOKE_OK: real allocation, F2/service, quote/cancel, explicit acknowledgement, first free reset, redistribution, paid price/insufficient coins, resize, Escape.");
            world.QueueFree();await Frames(2);GetTree().Quit();
        }
        catch(Exception error){await Capture("respec-failure");GD.PushError($"{error}; result={_result}, points={_state.StatPoints}");GetTree().Quit(1);}
    }
    private async Task Invest(ProgressionPresentation character,int points)
    {
        var previous=_state.StatPoints;character.Toggle();await Frames(3);
        character.FindChildren("*","TabContainer",true,false).OfType<TabContainer>().Single().CurrentTab=1;await Frames(3);
        var plus=character.FindChildren("*","Button",true,false).OfType<Button>().Where(b=>b.Text=="+").ToArray();
        for(var i=0;i<points;i++)await Click(plus[2]);
        var apply=character.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Применить");
        await Wait(()=>!apply.Disabled);await Click(apply);await Wait(()=>_state.StatPoints==previous-points);
        character.Toggle();await Frames(4);
    }
    private async Task<RespecView> OpenReset(WorldController world)
    {
        Input.ParseInputEvent(new InputEventKey{Keycode=Key.F2,PhysicalKeycode=Key.F2,Pressed=true});await Frames(2);
        Input.ParseInputEvent(new InputEventKey{Keycode=Key.F2,PhysicalKeycode=Key.F2,Pressed=false});await Frames(3);
        var quest=world.GetChildren().OfType<ProjectG.Quests.QuestPresentation>().Single();
        var tavern=quest.GetChildren().OfType<ProjectG.Combat.TavernPresentation>().Single();
        await Click(((TavernView)tavern.FindChild("TavernView",true,false)).GetNode<Button>("%Respec"));
        var reset=(RespecView)tavern.FindChild("RespecView",true,false);
        await Wait(()=>reset.IsVisibleInTree()&&!reset.GetNode<Label>("%Summary").Text.Contains("Полный сброс характеристик"));await Frames(3);return reset;
    }
    private async Task Wait(Func<bool> done)
    {var end=Time.GetTicksMsec()+20000;while(!done()&&Time.GetTicksMsec()<end)await Frames(1);if(!done())throw new TimeoutException("Respec smoke deadline.");}
    private async Task Frames(int n){for(var i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task ActivateWithKeyboard(Button button)
    {
        button.GrabFocus();
        Input.ParseInputEvent(new InputEventKey{Keycode=Key.Space,PhysicalKeycode=Key.Space,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventKey{Keycode=Key.Space,PhysicalKeycode=Key.Space,Pressed=false});await Frames(2);
    }
    private async Task Click(Button button)
    {
        var rect=button.GetGlobalRect();
        var p=button is CheckBox?rect.Position+new Vector2(16,rect.Size.Y/2):rect.GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion{Position=p,GlobalPosition=p});await Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=p,GlobalPosition=p});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=p,GlobalPosition=p});await Frames(2);
    }
    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var folder=OS.GetCmdlineUserArgs().Single(a=>a.StartsWith("--capture-dir="))[14..];GetViewport().GetTexture().GetImage().SavePng($"{folder}/{name}.png");
    }
}
