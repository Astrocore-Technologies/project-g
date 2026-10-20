using Content.Shared.Network;
using Godot;
using ProjectG.UI;
namespace ProjectG.Quests;

public partial class QuestPresentation
{
    private ActivityOrderView _dialogueActivities=null!,_journalActivities=null!;
    private ActivityState? _activities;
    private uint _activitySequence;
    private ActivityCommand? _activityPending;
    private double _activitySentAt;
    private string _activityNotice="";
    private void InitializeActivities(Control dialogue,Control journal)
    {
        _dialogueActivities=dialogue.GetNode<ActivityOrderView>("%DialogueActivities");
        _journalActivities=journal.GetNode<ActivityOrderView>("%JournalActivities");
        _dialogueActivities.AcceptRequested+=()=>SendActivity(ActivityAction.AcceptDelivery);
        _dialogueActivities.DeliverRequested+=()=>SendActivity(ActivityAction.Deliver);
        _network.ActivitiesReceived+=ApplyActivities;_network.ActivityResultReceived+=ActivityResult;
        if(_network.LatestActivities is {} state)ApplyActivities(state);else RenderActivities();
    }
    private void ApplyActivities(ActivityState state)
    {if(state.OwnerId!=_player.EntityId)return;_activities=state;RenderActivities();}
    private void RenderActivities()
    {
        if(_dialogueActivities is null)return;
        _dialogueActivities.Render(_activities,_speaker,_player.IsAlive,_activityPending is not null,_activityNotice);
        _journalActivities.Render(_activities,default,_player.IsAlive,_activityPending is not null,_activityNotice);
    }
    private void SendActivity(ActivityAction action)
    {
        if(_activities is not {} state||_activityPending is not null||!_player.IsAlive||!_speaker.IsValid)return;
        var order=action==ActivityAction.AcceptDelivery?state.Order+1:state.Order;
        if(order is 0 or >long.MaxValue)return;
        if(++_activitySequence==0)++_activitySequence;
        var command=new ActivityCommand(_activitySequence,action,_speaker,order);
        _activityPending=command;_activitySentAt=Now();_activityNotice="Ожидание подтверждения…";
        _network.SendActivity(command);RenderActivities();
    }
    private void ActivityResult(ActivityResult result)
    {
        if(_activityPending is not {} pending||result.Sequence!=pending.Sequence)return;
        _activityPending=null;
        _activityNotice=result.Outcome switch
        {
            QuestOutcome.Accepted=>pending.Action==ActivityAction.AcceptDelivery?"Заказ принят. Древесину нужно принести из своих запасов.":"Поставка завершена. Награда получена.",
            QuestOutcome.AlreadyProcessed=>"Этот заказ уже обработан. Повторного списания и награды нет.",
            QuestOutcome.TooFar=>"Подойдите к нужному NPC.",QuestOutcome.MissingMaterials=>"Не хватает материалов из ваших запасов.",
            QuestOutcome.Busy=>"Остановитесь и завершите бой или текущее действие.",_=>"Заказ сейчас недоступен. Откройте разговор заново."
        };RenderActivities();
    }
    private void TickActivities()
    {
        if(_activityPending is not null&&Now()-_activitySentAt>5)
        {_activityPending=null;_activityNotice="Ответ задерживается. Откройте разговор снова, чтобы проверить сохранённый заказ.";RenderActivities();}
    }
}
