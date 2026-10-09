using Content.Shared.Network;
using Godot;
using ProjectG.Combat;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Quests;

/// <summary>AOI-only NPCs and owner journal: buttons send intentions, never local rewards.</summary>
public partial class QuestPresentation : CanvasLayer
{
    private NetworkClient _network = null!;
    private PlayerController _player = null!;
    private readonly Dictionary<NetworkEntityId, (QuestNpcSpawn State, Node3D Visual)> _npcs = new();
    private Control _overlay = null!;
    private VBoxContainer _shortcuts = null!;
    private UiWindow _dialogue=null!,_journalWindow=null!;
    private UiModelPreview _speakerVisual=null!;
    private VBoxContainer _entries=null!;
    private Label _journalTitle=null!,_journalText=null!,_reward=null!,_journalStatus=null!,_tracker=null!;
    private Button _track=null!;
    private bool _tracking=true;
    private ProfessionState _profession;
    private int _filter;

    private Label _title = null!, _body = null!, _status = null!;
    private Button _accept = null!, _deliver = null!, _talk = null!;
    private Button _trainingSword = null!, _swordsman = null!;
    private uint _sequence, _pending;
    private QuestAction _pendingAction;
    private double _sentAt, _refresh;
    private bool _journalMode;
    private NetworkEntityId _speaker, _nearest;
    private QuestJournal? _journal;
    public void Initialize(NetworkClient network, PlayerController player)
    {
        _network = network; _player = player; Layer = 21;
        var root = new Control { Theme = GameUi.CreateTheme(), MouseFilter = Control.MouseFilterEnum.Ignore }; AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shortcuts = _shortcuts = new VBoxContainer { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -246, OffsetRight = -16, OffsetTop = -178, OffsetBottom = -106 }; root.AddChild(shortcuts);
        _talk = GameUi.Button("F2 · Поговорить", () => Send(QuestAction.Talk, _nearest)); _talk.FocusMode = Control.FocusModeEnum.None; shortcuts.AddChild(_talk);
        var journal = GameUi.Button("L · Журнал поручений", OpenJournal); journal.FocusMode = Control.FocusModeEnum.None; shortcuts.AddChild(journal);
        GameUi.CompactHud(shortcuts);
        _dialogue=new UiWindow { ToggleKey=Key.F2 }; root.AddChild(_dialogue); _dialogue.Build("Разговор",new(1140,620)); _dialogue.CloseRequested+=Close;
        _overlay=_dialogue;
        var scene=new HBoxContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; _dialogue.Body.AddChild(scene);
        var portrait=UiComposition.Card(scene,380);
        _speakerVisual=new UiModelPreview { SizeFlagsVertical=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(320,280) }; portrait.AddChild(_speakerVisual);
        _title=GameUi.Text("Собеседник",24); portrait.AddChild(_title);
        var dialogue=UiComposition.Card(scene,340);
        _body=UiComposition.Paragraph(dialogue,"",20); _body.SizeFlagsVertical=Control.SizeFlags.ExpandFill;
        _status=UiComposition.Paragraph(dialogue,"",14);
        _accept=GameUi.Button("Взяться за доставку",()=>Send(QuestAction.Accept,_speaker)); dialogue.AddChild(_accept);
        _deliver=GameUi.Button("Передать материалы",()=>Send(QuestAction.Deliver,_speaker)); dialogue.AddChild(_deliver);
        _trainingSword=GameUi.Button("Получить тренировочный меч",()=>Send(QuestAction.TrainingSword,_speaker)); dialogue.AddChild(_trainingSword);
        _swordsman=GameUi.Button("Рассмотреть профессию Мечника",()=> { Close(); GameUi.Navigate("profession"); }); dialogue.AddChild(_swordsman);
        dialogue.AddChild(GameUi.Button("Вернусь позже",Close));
        _journalWindow=new UiWindow { ToggleKey=Key.L }; root.AddChild(_journalWindow); _journalWindow.Build("Журнал путешествия",new(1180,656)); _journalWindow.CloseRequested+=Close;
        var page=UiComposition.Page(_journalWindow,"journal");
        var filters=new HBoxContainer(); page.AddChild(filters);
        string[] names=["Текущие","Завершённые","Слухи"];
        for(var i=0;i<names.Length;i++) { var index=i; filters.AddChild(GameUi.Button(names[i],()=> { _filter=index; RenderJournal(); })); }
        var journalColumns=new HBoxContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; page.AddChild(journalColumns);
        var list=UiComposition.Card(journalColumns,230); _entries=UiComposition.Scroll(list);
        var detail=UiComposition.Card(journalColumns,360);
        _journalTitle=UiComposition.Paragraph(detail,"Журнал поручений",24);
        _journalText=UiComposition.Paragraph(detail,"Получение журнала…",17); _journalText.SizeFlagsVertical=Control.SizeFlags.ExpandFill;
        _reward=UiComposition.Paragraph(detail,"",16);
        _track=GameUi.Button("Закрепить в HUD",()=> { _tracking=!_tracking; RenderJournal(); }); detail.AddChild(_track);
        detail.AddChild(GameUi.Button("Открыть карту",()=>GameUi.Navigate("map")));
        _journalStatus=UiComposition.Paragraph(page,"",14);
        var trackerPanel=new PanelContainer { Position=new(16,240),CustomMinimumSize=new(282,0),MouseFilter=Control.MouseFilterEnum.Ignore }; root.AddChild(trackerPanel);
        _tracker=UiComposition.Paragraph(trackerPanel,"",14); _tracker.CustomMinimumSize=new(254,0);
        GameUi.CompactHud(trackerPanel);
        network.QuestNpcReceived += Spawn; network.PlayerDespawned += Despawn;
        network.QuestReplyReceived += Reply; network.QuestJournalReceived += Journal;
        network.ProfessionReceived += Training;
        foreach (var npc in network.QuestNpcs.Values) Spawn(npc);
        if (network.LatestQuestJournal is { } state) Journal(state);
    }
    private void Spawn(QuestNpcSpawn npc)
    {
        if (_npcs.ContainsKey(npc.EntityId)) return;
        var actor = new Node3D { Name = $"QuestNpc-{npc.EntityId.Value}", Position = new(npc.Position.X, 0, npc.Position.Y) };
        AddChild(actor);
        if (npc.Role == "Наставник мечников") actor.AddChild(GD.Load<PackedScene>("res://Scenes/Actors/SwordTrainer.tscn").Instantiate<Node3D>());
        else actor.AddChild(new MeshInstance3D { Position = new(0, 1, 0), Mesh = new CapsuleMesh { Radius = .4f, Height = 2 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.67f, .55f, .34f) } });
        actor.AddChild(new Label3D { Position = new(0, 3.1f, 0), Text = npc.Name + "\n" + npc.Role,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, PixelSize = .006f, OutlineSize = 4, NoDepthTest = false });
        _npcs.Add(npc.EntityId, (npc, actor));
    }
    private void Despawn(PlayerDespawn value)
    {
        if (_npcs.Remove(value.EntityId, out var npc)) npc.Visual.QueueFree();
        if (_speaker == value.EntityId && !_journalMode) Close();
    }
    public override void _ExitTree()
    {
        GameUi.QuestWindowOpen = false;
        if (_network is null) return;
        _network.QuestNpcReceived -= Spawn; _network.PlayerDespawned -= Despawn; _network.QuestReplyReceived -= Reply; _network.QuestJournalReceived -= Journal;
        _network.ProfessionReceived -= Training;
    }
    public override void _Input(InputEvent ev)
    {
        if(GameUi.GameplayModalOpen) return;
        // NPC clicks are consumed before combat. GUI clicks cannot hit NPCs behind a panel.
        if (GameUi.GameplayModalOpen || !_player.IsAlive || ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse || GetViewport().GuiGetHoveredControl() is not null) return;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return;
        foreach (var (id, npc) in _npcs)
        {
            var point = npc.Visual.GlobalPosition + Vector3.Up;
            if (!camera.IsPositionBehind(point) && mouse.Position.DistanceTo(camera.UnprojectPosition(point)) < 24)
            { Send(QuestAction.Talk, id); GetViewport().SetInputAsHandled(); return; }
        }
    }
    public override void _UnhandledInput(InputEvent ev)
    {
        if (GameUi.GameplayModalOpen || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.PhysicalKeycode == Key.L) { OpenJournal(); GetViewport().SetInputAsHandled(); }
        else if (key.PhysicalKeycode == Key.F2 && _nearest.IsValid && _player.IsAlive) { Send(QuestAction.Talk, _nearest); GetViewport().SetInputAsHandled(); }
    }
    private bool Show(bool journal)
    {
        var target=journal?_journalWindow:_dialogue;
        if(!target.Open()) return false;
        _journalMode=journal; _overlay=target; GameUi.QuestWindowOpen=true;
        _accept.Visible=false; _deliver.Visible=false;
        _trainingSword.Visible=false; _swordsman.Visible=false;
        _player.StopMovement(); _player.GetNodeOrNull<CombatPresentation>("CombatPresentation")?.CancelAutoAttack();
        return true;
    }
    private void Close() { _dialogue.Close(); _journalWindow.Close(); GameUi.QuestWindowOpen=false; }
    public void OpenJournal()
    {
        if(_journalWindow.Visible) { Close(); return; }
        if(_pending!=0 || !Show(true)) return;
        RenderJournal(); Send(QuestAction.Journal,default);
    }
    private void Send(QuestAction action, NetworkEntityId npc)
    {
        if (GameUi.GameplayModalOpen && !_overlay.Visible || _pending != 0 || action != QuestAction.Journal && (!npc.IsValid || !_player.IsAlive)) return;
        if(action!=QuestAction.Journal)
        {
            if(!Show(false)) return; _speaker=npc; _title.Text="Разговор"; _body.Text="";
            if(_npcs.TryGetValue(npc,out var known))
            {
                _title.Text=known.State.Name;
                if (known.State.Role == "Наставник мечников") _speakerVisual.SetVisual(GD.Load<PackedScene>("res://Scenes/Actors/SwordTrainer.tscn"));
                else
                {
                    var mesh=known.Visual.GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
                    _speakerVisual.SetMesh(mesh?.Mesh,mesh?.MaterialOverride);
                }
            }
        }
        if (++_sequence == 0) ++_sequence; _pending = _sequence; _pendingAction = action; _sentAt = Now(); _status.Text = "Ожидание ответа…";
        _accept.Disabled = true; _deliver.Disabled = true; _network.SendQuest(new(_sequence, action, npc));
    }
    private void Reply(QuestReply reply)
    {
        if (reply.Sequence != _pending) return; _pending = 0;
        _status.Text = reply.Outcome switch
        {
            QuestOutcome.Accepted => _pendingAction switch
            {
                QuestAction.Accept => "Поручение принято. Журнал — L.",
                QuestAction.Deliver => "Доставка завершена. Награда получена.",
                QuestAction.TrainingSword => "Меч в инвентаре. Закройте разговор и наденьте его через I.",
                _ => ""
            },
            QuestOutcome.TooFar => "Подойдите ближе к собеседнику.",
            QuestOutcome.Busy => "Остановитесь и завершите текущее действие.",
            QuestOutcome.MissingMaterials => "Не хватает материалов. Их можно собрать заново.",
            QuestOutcome.MaterialFull => _pendingAction == QuestAction.TrainingSword ? "Инвентарь заполнен. Освободите одну ячейку и поговорите снова." : "Освободите место в запасе материалов.",
            QuestOutcome.AlreadyProcessed => "Это действие уже выполнено.",
            _ => "Сейчас действие недоступно."
        };
        if (_journalMode) { _journalStatus.Text=_status.Text; RenderJournal(); return; }
        _title.Text = reply.Speaker; _body.Text = reply.Text;
        _accept.Visible = (reply.Choices & 1) != 0; _deliver.Visible = (reply.Choices & 2) != 0;
        _trainingSword.Visible = (reply.Choices & 4) != 0; _swordsman.Visible = (reply.Choices & 8) != 0;
        _accept.Disabled = false; _deliver.Disabled = false;
    }
    private void Journal(QuestJournal journal)
    { if (journal.OwnerId != _player.EntityId) return; _journal = journal; RenderJournal(); }
    private void Training(ProfessionState state)
    {
        if (state.OwnerId != _player.EntityId) return;
        _profession = state; RenderJournal();
    }
    private void RenderJournal()
    {
        foreach(var child in _entries.GetChildren()) { _entries.RemoveChild(child); child.QueueFree(); }
        _reward.Text=""; _track.Visible=false;
        if(_journal is not {} j) { _journalTitle.Text="Поручения"; _journalText.Text="Получение журнала…"; return; }
        _tracker.Text=_tracking && j.Status==QuestStatus.Active?$"◆ {j.Title}\n{j.Objective}\n{j.Material}: {j.Carried} / {j.Required}":"L · Журнал путешествия";
        if (_profession.TrainingRequired>0 && _profession.ActiveId!=2)
            _tracker.Text=$"Обучение мечу: {Math.Floor(_profession.TrainingDamage):0}/{_profession.TrainingRequired:0}\n" +
                (_profession.TrainingDamage>=_profession.TrainingRequired ? "Вернитесь к тренеру на арене" : "Наденьте тренировочный меч · I");
        if(_filter==2)
        {
            _journalTitle.Text="Слухи в окрестностях";
            _journalText.Text=_network.LatestWorldNode is {} node?node.Rumor:"Пока нет известных слухов. Исследуйте мир и разговаривайте с жителями.";
            UiComposition.Paragraph(_entries,"Известные сведения",16); return;
        }
        var matches=_filter==0?j.Status==QuestStatus.Active:j.Status==QuestStatus.Completed;
        if(!matches)
        {
            _journalTitle.Text=_filter==0?"Текущие поручения":"Завершённые поручения";
            _journalText.Text=j.Status==QuestStatus.Unknown && _filter==0?j.Objective:_filter==0?"Сейчас нет активных поручений. Новую работу можно найти у жителей мира.":"Вы ещё не завершили поручений.";
            UiComposition.Paragraph(_entries,"Здесь пока пусто",14); return;
        }
        _entries.AddChild(GameUi.Button(j.Title,()=>RenderJournal()));
        _journalTitle.Text=j.Title;
        _journalText.Text=(j.Status==QuestStatus.Completed?"✓ Выполнено\n\n":"В работе\n\n")+j.Objective+$"\n\n{j.Material}: {(j.Status==QuestStatus.Completed?j.Required:j.Carried)} / {j.Required}";
        _reward.Text=$"{(j.Status==QuestStatus.Completed?"Награда получена":"Награда")}: {j.Experience} EXP";
        _track.Visible=j.Status==QuestStatus.Active; _track.Text=_tracking?"Убрать из HUD":"Закрепить в HUD";
    }
    public override void _Process(double delta)
    {
        _shortcuts.Visible = !GameUi.GameplayModalOpen; _tracker.GetParent<Control>().Visible=!GameUi.GameplayModalOpen;
        if (_overlay.Visible)
        {
            if (!_player.IsAlive && !_journalMode) Close();
        }
        if (_pending != 0 && Now() - _sentAt > 5) { _pending = 0; _status.Text = "Нет ответа. Закройте окно и попробуйте снова."; _journalStatus.Text=_status.Text; }
        if ((_refresh += delta) < .15) return; _refresh = 0;
        _nearest = default; var distance = 2.5f * 2.5f;
        foreach (var (id, npc) in _npcs)
        { var next = System.Numerics.Vector2.DistanceSquared(_player.PredictedPosition, npc.State.Position); if (next < distance) { distance = next; _nearest = id; } }
        _talk.Disabled = !_nearest.IsValid || !_player.IsAlive || _pending != 0;
        _talk.Text = _nearest.IsValid ? "F2 · " + _npcs[_nearest].State.Name : "F2 · Подойдите к NPC";
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;
}
