using Content.Shared.Network;
using Godot;
using ProjectG.Networking;
namespace ProjectG.UI;

/// <summary>Live screen navigation and the adventurers' notice board. No local quest or rank simulation.</summary>
public partial class GameNavigation : CanvasLayer
{
    private NetworkClient _network=null!;
    private NetworkEntityId _owner;
    private Control _hud=null!;
    private UiWindow _menu=null!,_guild=null!;
    private VBoxContainer _board=null!;
    private Control _notices=null!;
    private Label _noticeText=null!;
    private readonly Queue<string> _messages=new();
    private bool _partyActive;

    private Label _questTitle=null!,_questText=null!,_rewards=null!;
    public void Initialize(NetworkClient network,NetworkEntityId owner)
    {
        _network=network; _owner=owner; Layer=40;
        var root=new Control { Theme=GameUi.CreateTheme(),MouseFilter=Control.MouseFilterEnum.Ignore }; AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var navPanel=new PanelContainer { AnchorLeft=1,AnchorRight=1,OffsetLeft=-626,OffsetRight=-16,OffsetTop=12 }; root.AddChild(navPanel); _hud=navPanel;
        var nav=new HBoxContainer(); navPanel.AddChild(nav);
        foreach(var item in UiComposition.Screens)
        {
            var cell=new VBoxContainer { CustomMinimumSize=new(76,0) }; nav.AddChild(cell);
            var button=new UiIconSlot { CustomMinimumSize=new(48,48),SizeFlagsHorizontal=Control.SizeFlags.ShrinkCenter,FocusMode=Control.FocusModeEnum.None };
            button.Bind(item.Icon,item.Label+" · "+item.Key); button.Disabled=item.Id!="guild" && !GameUi.HasRoute(item.Id); button.Pressed+=()=>GameUi.Navigate(item.Id); cell.AddChild(button);
            var label=GameUi.Text(item.Id=="echo"?"Эхо":item.Id=="map"?"Карта":item.Label,12); label.HorizontalAlignment=HorizontalAlignment.Center; cell.AddChild(label);
        }
        var menuCell=new VBoxContainer { CustomMinimumSize=new(64,0) }; nav.AddChild(menuCell);
        var menuButton=new UiIconSlot { CustomMinimumSize=new(48,48),FocusMode=Control.FocusModeEnum.None }; menuButton.Bind("nav.menu","Меню · Esc"); menuButton.Pressed+=ToggleMenu; menuCell.AddChild(menuButton);
        var menuLabel=GameUi.Text("Меню",12); menuLabel.HorizontalAlignment=HorizontalAlignment.Center; menuCell.AddChild(menuLabel);
        var notices=new PanelContainer { AnchorTop=1,AnchorBottom=1,OffsetLeft=16,OffsetRight=296,OffsetTop=-186,OffsetBottom=-16,MouseFilter=Control.MouseFilterEnum.Stop }; root.AddChild(notices); _notices=notices;
        var feed=new VBoxContainer(); notices.AddChild(feed); feed.AddChild(GameUi.Text("События мира",16));
        _noticeText=UiComposition.Paragraph(UiComposition.Scroll(feed),"Здесь появятся вести и приглашения.",13); _noticeText.SizeFlagsVertical=Control.SizeFlags.ExpandFill;
        var actions=new HBoxContainer(); feed.AddChild(actions); actions.AddChild(GameUi.Button("Журнал",()=>GameUi.Navigate("journal"))); actions.AddChild(GameUi.Button("Группа",()=>GameUi.Navigate("party")));
        if(network.LatestWorldNode is {} known) WorldNotice(known);
        network.WorldNodeReceived+=WorldNotice; network.TradeStateReceived+=TradeNotice; network.SocialInvitesReceived+=InviteNotice; network.SocialRosterReceived+=PartyNotice; network.ProfessionReceived+=ProfessionNotice;
        _menu=new UiWindow { ToggleKey=Key.Escape }; root.AddChild(_menu); _menu.Build("Меню путешественника",new(730,620)); _menu.CloseRequested+=_menu.Close;
        var grid=new GridContainer { Columns=2,SizeFlagsVertical=Control.SizeFlags.ExpandFill }; _menu.Body.AddChild(grid);
        (string Id,string Text)[] links=[("character","Персонаж · K"),("inventory","Инвентарь · I"),("echo","Эхо Прошлого · F3"),("guild","Гильдия авантюристов · G"),("map","Карта мира · M"),("journal","Журнал · L"),("profession","Профессия · P"),("craft","Ремесло и ремонт · C"),("trade","Обмен · B"),("market","Кузница и рынок · J"),("party","Группа · N"),("clan","Гильдия игроков · O"),("pvp","PvP и возрождение · V"),("keeper","Хранитель · H"),("guide","Управление · F1")];
        foreach(var link in links) { var button=GameUi.Button(link.Text,()=>GameUi.Navigate(link.Id)); button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill; button.Disabled=!GameUi.HasRoute(link.Id) && link.Id!="guild"; grid.AddChild(button); }
        _menu.Body.AddChild(GameUi.Text("Мир продолжает жить, пока открыто меню.",14));
        _menu.Body.AddChild(GameUi.Button("Продолжить приключение",_menu.Close));
        _guild=new UiWindow { ToggleKey=Key.G }; root.AddChild(_guild); _guild.Build("Гильдия авантюристов",new(1180,656)); _guild.CloseRequested+=_guild.Close;
        var page=UiComposition.Page(_guild,"guild");
        var tabs=new HBoxContainer(); page.AddChild(tabs);
        tabs.AddChild(GameUi.Button("Доска поручений",Refresh));
        tabs.AddChild(GameUi.Button("Ранг и репутация",()=> { _questTitle.Text="Ранг авантюриста"; _questText.Text="Сведения о ранге и репутации гильдии пока недоступны."; _rewards.Text=""; }));
        tabs.AddChild(GameUi.Button("Гильдия игроков · O",()=>GameUi.Navigate("clan")));
        var columns=new HBoxContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill }; page.AddChild(columns);
        var board=UiComposition.Card(columns,260); board.AddChild(GameUi.Text("Известные поручения",20)); _board=UiComposition.Scroll(board);
        var detail=UiComposition.Card(columns,370); _questTitle=UiComposition.Paragraph(detail,"",24); _questText=UiComposition.Paragraph(detail,"",17); _questText.SizeFlagsVertical=Control.SizeFlags.ExpandFill;
        _rewards=UiComposition.Paragraph(detail,"",16);
        detail.AddChild(GameUi.Button("Открыть журнал",()=>GameUi.Navigate("journal")));
        detail.AddChild(GameUi.Button("Карта окрестностей",()=>GameUi.Navigate("map")));
        page.AddChild(GameUi.Text("Поручения принимаются у собеседника в мире · F2",14));
        GameUi.RegisterRoute("guild",ToggleGuild); network.QuestJournalReceived+=Quest; Refresh();
    }
    private void Notice(string text)
    {
        if(_messages.LastOrDefault()==text) return; _messages.Enqueue(text); while(_messages.Count>3) _messages.Dequeue(); _noticeText.Text=string.Join("\n\n",_messages);
    }
    private void WorldNotice(WorldNodeState state) => Notice(state.Rumor);
    private void TradeNotice(TradeState state) { if(state.OwnerId==_owner && state.Phase==TradePhase.Invited) Notice("Приглашение к обмену · B"); }
    private void InviteNotice(SocialInvites state) { if(state.Owner!=_owner) return; foreach(var invite in state.Items) Notice(invite.Kind==SocialKind.Party?"Приглашение в группу · N":"Приглашение в гильдию игроков · O"); }
    private void PartyNotice(SocialRoster state) { if(state.Owner==_owner && state.Kind==SocialKind.Party) _partyActive=state.Id!=0; }
    private void ProfessionNotice(ProfessionState state) { if(state.OwnerId==_owner && state.OfferedId!=0) Notice("Открылась профессия: "+state.OfferedName+" · P"); }
    private void Quest(QuestJournal value) { if(value.OwnerId==_owner) Refresh(); }
    private void Refresh()
    {
        foreach(var child in _board.GetChildren()) { _board.RemoveChild(child); child.QueueFree(); }
        _rewards.Text="";
        if(_network.LatestQuestJournal is not {} q || q.OwnerId!=_owner || q.Status==QuestStatus.Unknown)
        { _questTitle.Text="Начните знакомство с жителями"; _questText.Text=(_network.LatestQuestJournal is {} known && known.OwnerId==_owner?known.Objective:null)??"Известных поручений пока нет. Поговорите с жителями, чтобы найти работу."; UiComposition.Paragraph(_board,"Нет известных поручений",14); return; }
        _board.AddChild(GameUi.Button(q.Title,Refresh)); _questTitle.Text=q.Title;
        _questText.Text=(q.Status==QuestStatus.Completed?"✓ Выполнено\n\n":"В работе\n\n")+q.Objective;
        _rewards.Text=$"{(q.Status==QuestStatus.Completed?"Награда получена":"Награда")}: {q.Experience} EXP";
    }
    public void ToggleGuild() { if(_guild.Visible) _guild.Close(); else { Refresh(); _guild.Open(); } }
    public void ToggleMenu() { if(_menu.Visible) _menu.Close(); else _menu.Open(); }
    public override void _Process(double delta) { _hud.Visible=!GameUi.GameplayModalOpen; _notices.Visible=!GameUi.GameplayModalOpen && !_partyActive; }
    public override void _UnhandledInput(InputEvent ev)
    {
        if(GameUi.GameplayModalOpen || ev is not InputEventKey { Pressed:true,Echo:false } key) return;
        if(key.PhysicalKeycode==Key.Escape) { ToggleMenu(); GetViewport().SetInputAsHandled(); }
        if(key.PhysicalKeycode==Key.G) { ToggleGuild(); GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree() { GameUi.ClearRoutes(); if(_network is not null) { _network.QuestJournalReceived-=Quest; _network.WorldNodeReceived-=WorldNotice; _network.TradeStateReceived-=TradeNotice; _network.SocialInvitesReceived-=InviteNotice; _network.SocialRosterReceived-=PartyNotice; _network.ProfessionReceived-=ProfessionNotice; } }
}
