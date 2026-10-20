using Content.Shared.Network;
using Godot;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

/// <summary>Offline fixture adapter for the production dialogue and activity component.</summary>
public partial class CampActivitiesPreview:Node
{
    private ActivityOrderView _view=null!;private ActivityState _state;
    private UiWindow _window=null!;
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed;GetWindow().Size=new(1280,720);await Frames(3);
            _window=new();AddChild(_window);_window.Build("Разговор · автономный preview",new(1140,620));
            _window.CloseRequested+=_window.Close;
            var dialogue=GD.Load<PackedScene>("res://UI/Screens/DialogueView.tscn").Instantiate<Control>();_window.Body.AddChild(dialogue);
            foreach(var name in new[]{"Accept","Deliver","TrainingSword","Swordsman"})dialogue.GetNode<Button>("%"+name).Hide();
            dialogue.GetNode<Button>("%Close").Pressed+=_window.Close;
            dialogue.GetNode<Label>("%Speaker").Text="Лада · снабжение лагеря";
            dialogue.GetNode<Label>("%Body").Text="Для следующей поставки нужны три древесины из ваших запасов.";
            _view=dialogue.GetNode<ActivityOrderView>("%DialogueActivities");
            _state=new(new(1),1,"Древесина для лагеря","Возьмите заказ у Лады. Доставьте Орену к востоку от лагеря 3 древесины из своих запасов.","Древесина",3,3,20,0,QuestStatus.Unknown,new(11),new(12),1,CampWatchPhase.Active,20);
            _view.AcceptRequested+=Accept;_view.DeliverRequested+=Deliver;_window.Open();Render();
            var capture=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--capture-dir="))?[14..];
            if(capture is null)return;
            await Capture(capture,"camp-preview-available-1280");Accept();await Capture(capture,"camp-preview-deliver-1280");
            Deliver();GetWindow().Size=new(1920,1080);await Frames(5);await Capture(capture,"camp-preview-completed-1920");
            GD.Print("CAMP_ACTIVITIES_PREVIEW_OK");_window.Close();GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private void Accept(){_state=_state with {Order=_state.Order+1,Status=QuestStatus.Active,Carried=3};Render();}
    private void Deliver(){_state=_state with {Status=QuestStatus.Completed,Carried=0,WatchPhase=CampWatchPhase.Completed};Render();}
    private void Render()=>_view.Render(_state,_state.Status==QuestStatus.Active?_state.Recipient:_state.Giver,true,false,"Данные автономного preview.");
    public override void _ExitTree(){if(_view is null)return;_view.AcceptRequested-=Accept;_view.DeliverRequested-=Deliver;}
    private async Task Frames(int count){for(var i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Capture(string folder,string name)
    {await Frames(4);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng($"{folder}/{name}.png");}
}
