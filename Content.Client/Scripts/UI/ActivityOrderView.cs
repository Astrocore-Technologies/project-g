using Content.Shared.Network;
using Godot;
namespace ProjectG.UI;

/// <summary>Shared editable component for NPC dialogue and journal. No local reward computation.</summary>
public partial class ActivityOrderView:VBoxContainer
{
    private Label _title=null!,_summary=null!,_watch=null!,_notice=null!;
    private Button _accept=null!,_deliver=null!;
    public event Action? AcceptRequested,DeliverRequested;
    public override void _Ready()
    {
        _title=GetNode<Label>("Title");_summary=GetNode<Label>("Summary");_watch=GetNode<Label>("Watch");_notice=GetNode<Label>("Notice");
        _accept=GetNode<Button>("Actions/Accept");_deliver=GetNode<Button>("Actions/Deliver");
        _accept.Pressed+=()=>AcceptRequested?.Invoke();_deliver.Pressed+=()=>DeliverRequested?.Invoke();
    }
    public void Render(ActivityState? value,NetworkEntityId speaker,bool alive,bool pending,string notice="")
    {
        Visible=value is not null;if(value is not {} state)return;
        _title.Text=state.Title;
        var status=state.Status==QuestStatus.Active?$"Заказ №{state.Order}: {state.Material} {state.Carried}/{state.Required}":
            state.Status==QuestStatus.Completed?$"Заказ №{state.Order} выполнен. Можно взять следующий.":"Повторяемый заказ у снабженца.";
        _summary.Text=$"{status}\n{state.Objective}\nНаграда: {state.Experience} EXP · материалы из ваших запасов";
        _accept.Visible=speaker.IsValid&&speaker==state.Giver&&state.Status!=QuestStatus.Active;
        _deliver.Visible=speaker.IsValid&&speaker==state.Recipient&&state.Status==QuestStatus.Active;
        _accept.Disabled=!alive||pending||state.Order==long.MaxValue;
        _deliver.Disabled=!alive||pending||state.Carried<state.Required;
        _notice.Text=notice;
        _watch.Visible=state.WatchPhase!=CampWatchPhase.None;
        _watch.Text=state.WatchPhase==CampWatchPhase.Active?$"Дозор лагеря · запуск №{state.WatchOccurrence}\nСледующая победа над обычным NPC: дополнительный общий пул {state.WatchExperience} EXP.":
            $"Дозор лагеря №{state.WatchOccurrence} завершён. Бонусный пул уже распределён.";
    }
}
