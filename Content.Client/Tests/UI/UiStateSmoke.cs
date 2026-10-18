using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.Progression;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

/// <summary>Explicit DTO fixtures: same working profession view, without a transport connection.</summary>
public partial class UiStateSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed;GetWindow().Size=new(1280,720);
            using var network=new NetworkClient();var ui=new ProfessionPresentation();AddChild(ui);ui.Initialize(network);
            ui.Apply(new(new(1),1,0,"",2,"Мечник"));ui.Toggle();await Frames(3);
            var buttons=ui.FindChildren("*","Button",true,false).OfType<Button>().ToArray();
            var prepare=buttons.Single(b=>b.Name=="Prepare");var confirm=buttons.Single(b=>b.Name=="Confirm");
            await Click(prepare);if(!prepare.Disabled)throw new Exception("Pending profession remained actionable.");await UiReviewEvidence.Capture(this,"profession-pending");
            ui.Result(new(1,2,ProfessionOutcome.Prepared,97));await Frames(3);
            if(!confirm.Disabled)throw new Exception("Irreversible choice omitted acknowledgement.");
            var check=ui.FindChildren("Acknowledge","CheckBox",true,false).OfType<CheckBox>().Single();await Click(check);
            if(confirm.Disabled)throw new Exception("Acknowledged choice remained disabled.");
            var window=ui.GetChildren().OfType<UiWindow>().Single();var panel=window.FindChildren("Frame","PanelContainer",true,false).OfType<PanelContainer>().Single();
            if(!GetViewport().GetVisibleRect().Encloses(panel.GetGlobalRect()))throw new Exception("Profession confirmation overflows.");
            await UiReviewEvidence.Capture(this,"profession-confirmation");
            await Click(buttons.Single(b=>b.Name=="Cancel"));ui.Result(new(2,3,ProfessionOutcome.Cancelled,0));await Frames(2);
            if(check.ButtonPressed || confirm.IsVisibleInTree())throw new Exception("Cancellation retained confirmation state.");
            window.RequestClose();ui.QueueFree();await Frames(3);if(GameUi.GameplayModalOpen)throw new Exception("Fixture leaked modal.");
            UiReviewEvidence.Record("profession-states","PASS: offline DTO fixture, pending, irreversible acknowledgement, cancel/reset, bounds and cleanup");GD.Print("UI_STATES_OK");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private async Task Frames(int n){for(var i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Click(BaseButton b){var p=b.GetGlobalRect().GetCenter();Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Position=p,GlobalPosition=p,Pressed=true});await Frames(1);Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Position=p,GlobalPosition=p,Pressed=false});await Frames(2);}
}
