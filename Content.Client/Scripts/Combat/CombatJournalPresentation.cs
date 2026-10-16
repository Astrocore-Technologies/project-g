using System.Text;
using Content.Shared.Combat;
using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Combat;

public partial class CombatJournalPresentation:CanvasLayer
{
    private NetworkClient _network=null!;private NetworkEntityId _owner;private UiWindow _window=null!;
    private VBoxContainer _deaths=null!;private Label _text=null!,_status=null!;
    private readonly List<CombatJournalEntry> _current=new(200),_selectedEntries=new(300),_building=new(300);
    private DeathLogSummary[] _index=[];private ulong _selected;private uint _sequence,_pending;private ushort _total;
    private bool _dirty;private double _clock,_refresh,_sent;
    public void Initialize(NetworkClient network,NetworkEntityId owner)
    {
        _network=network;_owner=owner;Layer=35;_window=new();AddChild(_window);_window.Build("Журнал боя и разбор смерти",new(1180,656));_window.CloseRequested+=_window.Close;
        var actions=new HBoxContainer();_window.Body.AddChild(actions);
        actions.AddChild(GameUi.Button("Текущий бой",()=>Select(0)));actions.AddChild(GameUi.Button("Обновить",()=>Select(_selected)));
        actions.AddChild(GameUi.Text("Доступен только вам · время местное",14));
        _status=GameUi.Text("",14);_window.Body.AddChild(_status);
        var columns=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};_window.Body.AddChild(columns);
        var history=UiComposition.Card(columns,260);history.AddChild(GameUi.Text("Последние смерти",18));_deaths=UiComposition.Scroll(history);
        var content=UiComposition.Card(columns,650);_text=UiComposition.Paragraph(UiComposition.Scroll(content),"Событий пока нет.",14);_text.Name="CombatJournalText";
        network.CombatJournalIndexReceived+=Apply;network.CombatJournalChunkReceived+=Apply;network.CombatJournalResultReceived+=Result;
        if(network.LatestCombatJournalIndex is {} index)Apply(index);
    }
    public void Toggle(){if(_window.Visible)_window.Close();else if(_window.Open())Select(0);}
    public void OpenLatestDeath(){if(_window.Open())Select(_index.Length>0?_index[^1].DeathId:0);}
    public void Apply(CombatJournalIndex value)
    {
        if(value.OwnerId!=_owner)return;_index=value.Deaths;
        foreach(var child in _deaths.GetChildren()){_deaths.RemoveChild(child);child.QueueFree();}
        if(_index.Length==0)_deaths.AddChild(GameUi.Text("Сохранённых разборов нет.",14));
        for(var i=_index.Length-1;i>=0;i--)
        {
            var death=_index[i];var at=DateTimeOffset.FromUnixTimeMilliseconds(death.AtUnixMilliseconds).ToLocalTime();
            _deaths.AddChild(GameUi.Button($"{at:dd.MM.yyyy HH:mm:ss}\nСобытий: {death.EventCount}",()=>Select(death.DeathId)));
        }
    }
    public void Select(ulong death)
    {
        _selected=death;_selectedEntries.Clear();_building.Clear();_total=0;
        if(++_sequence==0)++_sequence;_pending=_sequence;_sent=_clock;_status.Text="Загрузка подтверждённого журнала…";_dirty=true;
        _network.SendCombatJournalRequest(new(_sequence,death));
    }
    public void Apply(CombatJournalChunk value)
    {
        if(value.OwnerId!=_owner)return;
        if(value.Sequence==0)
        {
            foreach(var entry in value.Entries)
            {if(_current.Count>0&&entry.Id<=_current[^1].Id)continue;if(_current.Count==200)_current.RemoveAt(0);_current.Add(entry);}
            if(_selected==0)_dirty=true;return;
        }
        if(value.Sequence!=_pending || value.DeathId!=_selected)return;
        if(value.Start!=_building.Count || value.Start!=0&&value.Total!=_total)
        {Incomplete();return;}
        _total=value.Total;
        foreach(var entry in value.Entries)
        {
            if(_building.Count>0&&(entry.Id<=_building[^1].Id || entry.AtUnixMilliseconds<_building[^1].AtUnixMilliseconds)){Incomplete();return;}
            _building.Add(entry);
        }
        if(_building.Count!=_total)return;
        if(_selected>0&&(_building.Count==0 || _building[^1].Kind!=CombatJournalKind.Death)){Incomplete();return;}
        var destination=_selected==0?_current:_selectedEntries;destination.Clear();destination.AddRange(_building);
        _building.Clear();_pending=0;_status.Text=_selected==0?"Последние 200 событий текущего сеанса.":"Последние 30 секунд перед смертью · до 300 событий.";_dirty=true;
    }
    private void Incomplete(){_building.Clear();_pending=0;_status.Text="Ответ журнала неполон. Нажмите «Обновить».";}
    private void Result(CombatJournalResult value)
    {if(value.Sequence!=_pending)return;_pending=0;_building.Clear();_status.Text=value.Outcome==CombatJournalOutcome.NotFound?"Этот разбор уже не хранится.":"Слишком частые запросы. Повторите позже.";}
    public override void _Process(double delta)
    {
        _clock+=delta;if(_pending!=0&&_clock-_sent>5){_pending=0;_building.Clear();_status.Text="Нет ответа. Нажмите «Обновить».";}
        if(!_window.Visible || !_dirty || _clock<_refresh)return;_dirty=false;_refresh=_clock+.2;
        var entries=_selected==0?_current:_selectedEntries;var text=new StringBuilder();
        foreach(var entry in entries)
        {
            var at=DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMilliseconds).ToLocalTime();
            text.Append(at.ToString("HH:mm:ss.fff")).Append(" · ").Append(entry.Source).Append(" → ").Append(entry.Target).Append(" · ").Append(Describe(entry));
            if(entry.SkillId!=0)text.Append(" · навык #").Append(entry.SkillId);text.AppendLine();
        }
        _text.Text=text.Length==0?"Событий пока нет.":text.ToString();
    }
    private static string Describe(CombatJournalEntry e)
    {
        var result=e.Kind switch
        {
            CombatJournalKind.Death=>"СМЕРТЬ",
            CombatJournalKind.Resurrection=>$"Воскрешение +{e.Amount:0.#} HP",
            CombatJournalKind.Respawn=>$"Возрождение: {e.Amount:0.#} HP",
            CombatJournalKind.Healing=>$"Лечение +{e.Amount:0.#} HP",
            CombatJournalKind.ShieldApplied=>$"Щит: {e.Amount:0.#}",
            CombatJournalKind.ShieldAbsorption=>$"Поглощено щитом: {e.Amount:0.#}",
            CombatJournalKind.LifeSteal=>$"Похищено +{e.Amount:0.#} HP",
            CombatJournalKind.ManaSteal=>$"Похищено +{e.Amount:0.#} MP",
            CombatJournalKind.HealthPrice=>$"Цена: −{e.Amount:0.#} HP",
            CombatJournalKind.Evasion=>"Числовое уклонение",
            CombatJournalKind.Parry=>"Парирование",
            CombatJournalKind.Absorbed=>"Без потери HP",
            CombatJournalKind.Control=>$"{ControlName(e.Control)} · {e.Duration:0.##} с"+
                (e.Control==CrowdControlKind.Slow?$" · {e.Strength:P0}":e.Control==CrowdControlKind.Blind?$" · −{e.Strength:0.#} точности":""),
            CombatJournalKind.ControlRemoved=>$"Снят контроль: {ControlName(e.Control)} · {e.Amount:0} эффект(а)",
            CombatJournalKind.EffectsCleansed=>$"Очищено отрицательных эффектов: {e.Amount:0}",
            CombatJournalKind.EffectsDispelled=>$"Рассеяно положительных эффектов: {e.Amount:0}",
            CombatJournalKind.PeriodicDamage=>$"Периодический урон −{e.Amount:0.#} HP",
            CombatJournalKind.ReflectedDamage=>$"Отражённый урон −{e.Amount:0.#} HP",
            CombatJournalKind.ActionActivated=>$"Активация · {e.Amount:0.#} MP",
            CombatJournalKind.ActionCancelled=>"Добровольная отмена",
            CombatJournalKind.ActionInterrupted=>$"Прерывание · {e.Amount:0.#} MP",
            _=>$"Урон −{e.Amount:0.#} HP"
        };
        if((e.Flags&CombatJournalFlags.Critical)!=0)result+=" · КРИТ";
        if((e.Flags&CombatJournalFlags.Blocked)!=0)result+=" · БЛОК";
        if((e.Flags&CombatJournalFlags.GuardBroken)!=0)result+=" · БЛОК ПРОБИТ";
        if(e.Kind is CombatJournalKind.Damage or CombatJournalKind.PeriodicDamage or CombatJournalKind.ReflectedDamage)
            result+=(e.Flags&CombatJournalFlags.Magical)!=0?" · магический":" · физический";
        return result;
    }
    private static string ControlName(CrowdControlKind kind)=>kind switch
    {CrowdControlKind.Stun=>"Оглушение",CrowdControlKind.Root=>"Обездвиживание",CrowdControlKind.Slow=>"Замедление",CrowdControlKind.Silence=>"Молчание",
        CrowdControlKind.Disarm=>"Разоружение",CrowdControlKind.Knockback=>"Отбрасывание",CrowdControlKind.Knockdown=>"Сбит с ног",CrowdControlKind.Fear=>"Страх",
        CrowdControlKind.Sleep=>"Сон",CrowdControlKind.Blind=>"Ослепление",CrowdControlKind.Taunt=>"Провокация",CrowdControlKind.Pull=>"Притягивание",CrowdControlKind.Freeze=>"Заморозка",_=>"Подброс"};
    public override void _ExitTree()
    {if(_network is not null){_network.CombatJournalIndexReceived-=Apply;_network.CombatJournalChunkReceived-=Apply;_network.CombatJournalResultReceived-=Result;}}
}
