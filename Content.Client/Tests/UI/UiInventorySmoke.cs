using Godot;
using Content.Shared.Network;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.Items;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

public partial class UiInventorySmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed; GetWindow().Size=new(1280,720);
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false; AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient"); InventoryState state=default;
            network.InventoryReceived+=value=>state=value; network.ConnectToServer();
            await Wait(()=>state.Items is { Count:>0 }); await Frames(5);
            var inventory=world.GetChildren().OfType<InventoryPresentation>().Single(); inventory.Toggle(); await Frames(5);
            if(!GameUi.GameplayModalOpen) throw new Exception("Inventory input lock missing.");
            var screen=GetViewport().GetVisibleRect();
            foreach(var control in inventory.FindChildren("*","Button",true,false).OfType<Button>().Where(x=>x.IsVisibleInTree()))
            { var rect=control.GetGlobalRect(); if(!screen.Encloses(rect)) throw new Exception($"Control outside viewport: {control.Text} {rect}"); }
            var heading=inventory.FindChildren("*","Label",true,false).OfType<Label>().Single(x=>x.Text=="Инвентарь");
            if(!screen.Encloses(heading.GetGlobalRect())) throw new Exception("Inventory heading is clipped.");
            var item=state.Items[0]; var action=inventory.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==(item.Equipped?"Снять":"Надеть"));
            if(OS.GetCmdlineUserArgs().Contains("--capture")) { await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/ui-foundation/inventory-live.png"); }
            await Click(action); await Wait(()=>state.Items.Any(x=>x.Handle==item.Handle && x.Equipped!=item.Equipped));
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode=Key.I,Keycode=Key.I,Pressed=true }); await Frames(2);
            if(GameUi.GameplayModalOpen) throw new Exception("I did not close inventory.");
            inventory.Toggle(); world.QueueFree(); await Frames(4);
            if(GameUi.GameplayModalOpen) throw new Exception("Disconnect leaked inventory modal.");
            GD.Print("UI_INVENTORY_OK: real owner state, grid, equip acknowledgement, I close and teardown."); GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task Frames(int count) { for(var i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    private async Task Wait(Func<bool> condition) { var end=Time.GetTicksMsec()+15000; while(!condition() && Time.GetTicksMsec()<end) await Frames(1); if(!condition()) throw new TimeoutException("Inventory smoke"); }
    private async Task Click(Button button)
    {
        var point=button.GetGlobalRect().GetCenter(); Input.WarpMouse(point); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point }); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point }); await Frames(2);
    }
}
