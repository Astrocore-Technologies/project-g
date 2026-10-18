using Content.Shared.Network;
using Godot;
using ProjectG.UI;

namespace ProjectG.Echoes;

public partial class EchoControls
{
    private EchoRosterPage? _roster;
    private ulong _selectedOwned;
    private uint _rosterSequence,_summonSequence,_recallSequence,_summonControlSequence,_restoreSequence;
    private bool _restorePending;
    private string _rosterNotice="";
    private bool _summonPending;

    private void RequestRoster(uint page)=>_network.SendEchoRoster(new(++_rosterSequence,page));
    private void ApplyRoster(EchoRosterPage value)
    {
        if(value.OwnerId!=_owner)return;
        _roster=value;
        foreach(var entry in value.Entries)
            if(entry.ActionSequence!=0&&(_summonSequence==0||Content.Shared.Movement.MovementSimulation.IsSequenceNewer(entry.ActionSequence,_summonSequence)))
                _summonSequence=entry.ActionSequence;
        if(!value.Entries.Any(e=>e.Handle==_selectedOwned))_selectedOwned=value.Entries.FirstOrDefault().Handle;
        Render();
    }
    private bool RenderRoster()
    {
        if(_roster is not {} roster)return false;
        foreach(var entry in roster.Entries)
        {
            var row=entry;
            _list.AddChild(GameUi.Button($"{row.Name}\n{AvailabilityText(row.Availability)}",()=>{_selectedOwned=row.Handle;Render();}));
        }
        if(roster.Page>0)_list.AddChild(GameUi.Button("← Назад",()=>RequestRoster(roster.Page-1)));
        if(((ulong)roster.Page+1)*NetworkConstants.EchoRosterPageSize<roster.Total)
            _list.AddChild(GameUi.Button("Далее →",()=>RequestRoster(roster.Page+1)));
        var selected=roster.Entries.FirstOrDefault(e=>e.Handle==_selectedOwned);
        if(selected.Handle==0)
        {_preview.Visible=false;_name.Text="Эхо Прошлого";_details.Text="У вас пока нет Эхо. Сначала нужно получить спутника.";return true;}
        _name.Text=selected.Name;_preview.Visible=false;
        if(selected.Availability==EchoAvailability.Active)
        {
            _selected=selected.Slot;
            if(_known.TryGetValue(selected.Slot,out var body)){_preview.Visible=true;_preview.SetMesh(body.Mesh,body.Material);}
        }
        _details.Text=$"{AvailabilityText(selected.Availability)}\nHP {selected.Health:0.#} · MP {selected.Mana:0.#} · Stamina {selected.Stamina:0.#}\n\n"+
            (selected.Availability==EchoAvailability.Active?$"Особая способность · {PcInput.Label(PcInput.Echoes[selected.Slot-1])}\nНаправьте курсор в мир и нажмите клавишу спутника.":
                selected.Availability==EchoAvailability.RequiresRestoration?"Для восстановления нужна Печать восстановления. После восстановления потребуется обычный призыв.":
                selected.Availability==EchoAvailability.Summoning?"Идёт призыв. Эхо появится после завершения подготовки.":"Призыв: 10 секунд и 20 MP. Одновременно могут быть активны три Эхо.")+"\n\n"+_rosterNotice;
        if((selected.Refusal&EchoRefusal.Basic)!=0)_details.Text+="\nСейчас отказывается от обычных атак.";
        if((selected.Refusal&EchoRefusal.Signature)!=0)_details.Text+="\nСейчас отказывается применять особую способность.";
        var summon=GameUi.Button("Призвать · 10 с · 20 MP",()=>RequestSummon(selected.Handle));
        summon.Disabled=!_alive||_summonPending||selected.Availability!=EchoAvailability.Ready;
        _list.AddChild(summon);
        if(selected.Availability==EchoAvailability.Summoning)
            _list.AddChild(GameUi.Button("Отменить призыв",()=>_network.SendEchoSummonControl(new(++_summonControlSequence,selected.ActionSequence,selected.Execution))));
        if(selected.Availability==EchoAvailability.Active)
        {
            var recall=GameUi.Button("Отозвать · только вне боя",()=>_network.SendEchoRecall(new(++_recallSequence,selected.Handle)));
            recall.Disabled=!_alive;_list.AddChild(recall);
        }
        if(selected.Availability==EchoAvailability.RequiresRestoration)
        {
            var restore=GameUi.Button($"Восстановить · печать ×{roster.RestorationCost} · вне боя",()=>
            {_restorePending=true;_rosterNotice="Ожидаем подтверждение восстановления…";_network.SendEchoRestore(new(++_restoreSequence,selected.Handle,selected.DeathId));Render();});
            restore.Disabled=!_alive||_restorePending;_list.AddChild(restore);
        }
        return true;
    }
    private void RequestSummon(ulong handle)
    {
        if(_summonPending||!_alive)return;
        _summonPending=true;_rosterNotice="Ожидаем подтверждение призыва…";
        _network.SendEchoSummon(new(++_summonSequence,handle));Render();
    }
    private void ApplySummon(EchoSummonResult value)
    {
        if(value.Sequence!=_summonSequence)return;
        _summonPending=false;
        _rosterNotice=value.Outcome switch
        {
            EchoSummonOutcome.Accepted=>"Призыв начат.",EchoSummonOutcome.Buffered=>"Призыв ожидает доступного окна действия.",
            EchoSummonOutcome.Completed=>"Эхо призвано.",EchoSummonOutcome.Cancelled=>"Призыв отменён.",
            EchoSummonOutcome.Interrupted=>"Призыв прерван.",EchoSummonOutcome.NoMana=>"Недостаточно MP для призыва.",
            EchoSummonOutcome.ActiveLimit=>"Уже активны три Эхо.",EchoSummonOutcome.AlreadyActive=>"Это Эхо уже активно.",
            EchoSummonOutcome.InvalidEcho=>"Эхо недоступно. Обновите список.",_=>"Сейчас нельзя призвать это Эхо."
        };
        Render();
    }
    private static string AvailabilityText(EchoAvailability value)=>value switch
    {EchoAvailability.Ready=>"Готово к призыву",EchoAvailability.Summoning=>"Призывается",EchoAvailability.Active=>"Активно",_=>"Требует восстановления"};
    private void ApplyRecall(EchoRecallResult value)
    {
        if(value.Sequence!=_recallSequence)return;
        _rosterNotice=value.Outcome switch {EchoRecallOutcome.Completed=>"Эхо отозвано. Состояние сохранено.",
            EchoRecallOutcome.InCombat=>"В бою нельзя отозвать Эхо.",_=>"Сейчас нельзя отозвать это Эхо."};Render();
    }
    private void ApplyRestore(EchoRestoreResult value)
    {
        if(value.Sequence!=_restoreSequence)return;_restorePending=false;
        _rosterNotice=value.Outcome switch
        {EchoRestoreOutcome.Completed=>"Эхо восстановлено. Теперь его можно призвать.",EchoRestoreOutcome.InCombat=>"В бою нельзя восстановить Эхо.",
            EchoRestoreOutcome.MissingSeal=>"Нужна Печать восстановления в инвентаре.",EchoRestoreOutcome.StaleDeath=>"Состояние Эхо изменилось. Обновите список.",
            EchoRestoreOutcome.Busy=>"Дождитесь завершения текущей операции.",_=>"Сейчас нельзя восстановить это Эхо."};Render();
    }
    public override void _ExitTree()
    {
        if(_network is null)return;
        DisposeSignatureInput();
        _network.EchoRosterReceived-=ApplyRoster;_network.EchoSummonReceived-=ApplySummon;_network.EchoRecallReceived-=ApplyRecall;
        _network.EchoRestoreReceived-=ApplyRestore;
    }
}
