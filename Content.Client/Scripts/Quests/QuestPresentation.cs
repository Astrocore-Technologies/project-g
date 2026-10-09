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
    private PanelContainer _panel = null!;
    private Label _title = null!, _body = null!, _status = null!;
    private Button _accept = null!, _deliver = null!, _talk = null!;
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
        var shortcuts = new VBoxContainer { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -246, OffsetRight = -16, OffsetTop = -178, OffsetBottom = -106 }; root.AddChild(shortcuts);
        _talk = GameUi.Button("F2 · Поговорить", () => Send(QuestAction.Talk, _nearest)); _talk.FocusMode = Control.FocusModeEnum.None; shortcuts.AddChild(_talk);
        var journal = GameUi.Button("L · Журнал поручений", OpenJournal); journal.FocusMode = Control.FocusModeEnum.None; shortcuts.AddChild(journal);
        _overlay = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop }; root.AddChild(_overlay); _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new(0, 0, 0, .5f), MouseFilter = Control.MouseFilterEnum.Ignore }; _overlay.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel = new PanelContainer(); _overlay.AddChild(_panel);
        var column = new VBoxContainer(); _panel.AddChild(column);
        _title = GameUi.Text("Поручения", 24); column.AddChild(_title);
        _body = GameUi.Text("", 18); _body.AutowrapMode = TextServer.AutowrapMode.WordSmart; _body.SizeFlagsVertical = Control.SizeFlags.ExpandFill; column.AddChild(_body);
        _status = GameUi.Text("", 14); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(_status);
        _accept = GameUi.Button("Взяться за доставку", () => Send(QuestAction.Accept, _speaker)); column.AddChild(_accept);
        _deliver = GameUi.Button("Передать материалы", () => Send(QuestAction.Deliver, _speaker)); column.AddChild(_deliver);
        column.AddChild(GameUi.Button("Закрыть · Esc", Close));
        network.QuestNpcReceived += Spawn; network.PlayerDespawned += Despawn;
        network.QuestReplyReceived += Reply; network.QuestJournalReceived += Journal;
        foreach (var npc in network.QuestNpcs.Values) Spawn(npc);
        if (network.LatestQuestJournal is { } state) Journal(state);
    }
    private void Spawn(QuestNpcSpawn npc)
    {
        if (_npcs.ContainsKey(npc.EntityId)) return;
        var actor = new Node3D { Name = $"QuestNpc-{npc.EntityId.Value}", Position = new(npc.Position.X, 0, npc.Position.Y) };
        AddChild(actor);
        actor.AddChild(new MeshInstance3D { Position = new(0, 1, 0), Mesh = new CapsuleMesh { Radius = .4f, Height = 2 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.67f, .55f, .34f) } });
        actor.AddChild(new Label3D { Position = new(0, 3.1f, 0), Text = npc.Name + "\n" + npc.Role,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = .015f, OutlineSize = 6, NoDepthTest = false });
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
    }
    public override void _Input(InputEvent ev)
    {
        if (_overlay.Visible)
        {
            if (ev is InputEventKey { Pressed: true, Echo: false } key)
            {
                if (key.PhysicalKeycode is Key.Escape or Key.L) Close();
                if (key.PhysicalKeycode != Key.Tab) GetViewport().SetInputAsHandled();
            }
            return;
        }
        // NPC clicks are consumed before combat. GUI clicks cannot hit NPCs behind a panel.
        if (GameUi.CharacterWindowOpen || !_player.IsAlive || ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse || GetViewport().GuiGetHoveredControl() is not null) return;
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
        if (GameUi.CharacterWindowOpen || ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.PhysicalKeycode == Key.L) { OpenJournal(); GetViewport().SetInputAsHandled(); }
        else if (key.PhysicalKeycode == Key.F2 && _nearest.IsValid && _player.IsAlive) { Send(QuestAction.Talk, _nearest); GetViewport().SetInputAsHandled(); }
    }
    private void Show(bool journal)
    {
        _journalMode = journal; _overlay.Visible = true; GameUi.QuestWindowOpen = true;
        _accept.Visible = false; _deliver.Visible = false;
        _player.StopMovement(); _player.GetNodeOrNull<CombatPresentation>("CombatPresentation")?.CancelAutoAttack();
    }
    private void Close() { _overlay.Visible = false; GameUi.QuestWindowOpen = false; }
    private void OpenJournal()
    {
        if (_pending != 0 || GameUi.CharacterWindowOpen) return;
        Show(true); RenderJournal(); Send(QuestAction.Journal, default);
    }
    private void Send(QuestAction action, NetworkEntityId npc)
    {
        if (GameUi.CharacterWindowOpen || _pending != 0 || action != QuestAction.Journal && (!npc.IsValid || !_player.IsAlive)) return;
        if (action != QuestAction.Journal) { Show(false); _speaker = npc; _title.Text = "Разговор"; _body.Text = ""; }
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
                _ => ""
            },
            QuestOutcome.TooFar => "Подойдите ближе к собеседнику.",
            QuestOutcome.Busy => "Остановитесь и завершите текущее действие.",
            QuestOutcome.MissingMaterials => "Не хватает материалов. Их можно собрать заново.",
            QuestOutcome.MaterialFull => "Освободите место в запасе материалов.",
            QuestOutcome.AlreadyProcessed => "Это действие уже выполнено.",
            _ => "Сейчас действие недоступно."
        };
        if (_journalMode) { RenderJournal(); return; }
        _title.Text = reply.Speaker; _body.Text = reply.Text;
        _accept.Visible = (reply.Choices & 1) != 0; _deliver.Visible = (reply.Choices & 2) != 0;
        _accept.Disabled = false; _deliver.Disabled = false;
    }
    private void Journal(QuestJournal journal)
    { if (journal.OwnerId != _player.EntityId) return; _journal = journal; if (_journalMode) RenderJournal(); }
    private void RenderJournal()
    {
        if (_journal is not { } j) { _title.Text = "Журнал поручений"; _body.Text = "Получение журнала…"; return; }
        _title.Text = j.Title;
        _body.Text = j.Status == QuestStatus.Unknown ? j.Objective :
            (j.Status == QuestStatus.Completed ? "Выполнено\n\n" : "В работе\n\n") + j.Objective +
            $"\n\n{j.Material}: {(j.Status == QuestStatus.Completed ? j.Required : j.Carried)} / {j.Required}\n{(j.Status == QuestStatus.Completed ? "Награда получена" : "Награда")}: {j.Experience} EXP";
    }
    public override void _Process(double delta)
    {
        if (_overlay.Visible)
        {
            var screen = GetViewport().GetVisibleRect().Size; _panel.Size = new(Math.Min(620, screen.X - 32), Math.Min(430, screen.Y - 32)); _panel.Position = (screen - _panel.Size) / 2;
            if (!_player.IsAlive && !_journalMode) Close();
        }
        if (_pending != 0 && Now() - _sentAt > 5) { _pending = 0; _status.Text = "Нет ответа. Закройте окно и попробуйте снова."; }
        if ((_refresh += delta) < .15) return; _refresh = 0;
        _nearest = default; var distance = 2.5f * 2.5f;
        foreach (var (id, npc) in _npcs)
        { var next = System.Numerics.Vector2.DistanceSquared(_player.PredictedPosition, npc.State.Position); if (next < distance) { distance = next; _nearest = id; } }
        _talk.Disabled = !_nearest.IsValid || !_player.IsAlive || _pending != 0;
        _talk.Text = _nearest.IsValid ? "F2 · " + _npcs[_nearest].State.Name : "F2 · Подойдите к NPC";
    }
    private static double Now() => Time.GetTicksMsec() / 1000d;
}
