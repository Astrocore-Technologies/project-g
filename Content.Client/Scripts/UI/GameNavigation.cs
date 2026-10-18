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
    private Button _notice=null!;
    private string _noticeRoute="";
    private double _noticeUntil;
    private UiWindow _settings = null!;
    private readonly Dictionary<Button,string> _bindingLabels = new();

    private Label _questTitle=null!,_questText=null!,_rewards=null!;
    private Button _logout=null!;
    private Label _logoutStatus=null!;
    public void Initialize(NetworkClient network,NetworkEntityId owner,string settingsPath="user://client-settings.cfg")
    {
        _network=network; _owner=owner; Layer=40;
        var root=new Control { Theme=GameUi.CreateTheme(),MouseFilter=Control.MouseFilterEnum.Ignore }; AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var navigation=GD.Load<PackedScene>("res://UI/Components/HudNavigation.tscn").Instantiate<Control>(); root.AddChild(navigation); _hud=navigation;
        (string Node,string Route,string Icon,string Label)[] shortcuts=[("Inventory","inventory","nav.inventory","Инвентарь · I"),("Character","character","nav.character","Персонаж · K"),("Journal","journal","nav.quests","Журнал · L"),("Guild","guild","icon.guild","Гильдия авантюристов · G"),("Map","map","nav.map","Карта · M")];
        foreach(var item in shortcuts)
        {
            var button=navigation.GetNode<Button>("%"+item.Node); button.Icon=UiAssets.Texture(item.Icon); button.TooltipText=item.Label;
            button.Disabled=item.Route!="guild" && !GameUi.HasRoute(item.Route); button.Pressed+=()=>GameUi.Navigate(item.Route);
            _bindingLabels[button]=item.Route;
        }
        var menuButton=navigation.GetNode<Button>("%Menu"); menuButton.Icon=UiAssets.Texture("nav.menu"); menuButton.TooltipText="Меню · Esc"; menuButton.Pressed+=ToggleMenu;
        _notice=navigation.GetNode<Button>("%Notice"); _notice.Pressed+=()=>GameUi.Navigate(_noticeRoute);
        network.TradeStateReceived+=TradeNotice; network.SocialInvitesReceived+=InviteNotice; network.ProfessionReceived+=ProfessionNotice;
        _menu=new UiWindow { ToggleKey=Key.Escape }; root.AddChild(_menu); _menu.Build("Меню путешественника",new(730,620)); _menu.CloseRequested+=_menu.Close;
        var menuView=GD.Load<PackedScene>("res://UI/Screens/PauseMenuView.tscn").Instantiate<Control>(); _menu.Body.AddChild(menuView);
        _settings=new UiWindow();root.AddChild(_settings);_settings.Build("Настройки",new(760,680));
        var settingsView=GD.Load<PackedScene>("res://UI/Screens/InGameSettingsView.tscn").Instantiate<ClientSettingsPanel>();settingsView.SettingsPath=settingsPath;_settings.Body.AddChild(settingsView);
        settingsView.CloseRequested+=()=>{settingsView.Commit();_settings.Close();};
        _settings.CloseRequested+=()=>{settingsView.Commit();_settings.Close();};
        GameUi.RegisterRoute("settings",()=>_settings.Open());
        string[] routes=["character","inventory","echo","guild","map","journal","combat","profession","craft","trade","market","party","clan","pvp","keeper","guide","settings","emotes"];
        foreach(var route in routes)
        {
            var id=route; var button=menuView.GetNode<Button>("%Route_"+id); button.Visible=GameUi.HasRoute(id) || id is "guild" or "emotes";
            button.Pressed+=()=>GameUi.Navigate(id);
            if(PcInput.Actions.Any(a=>a.Id==id))_bindingLabels[button]=id;
        }
        var more=menuView.GetNode<Control>("%More"); menuView.GetNode<Button>("%ToggleMore").Pressed+=()=>more.Visible=!more.Visible;
        menuView.GetNode<Button>("%Continue").Pressed+=_menu.Close;
        _logoutStatus=menuView.GetNode<Label>("%LogoutStatus");
        _logout=menuView.GetNode<Button>("%LogoutButton"); _logout.Pressed+=()=>
        { if(_network.RequestLogout()) { _logout.Disabled=true; _logoutStatus.Text="Сохранение персонажа…"; } };
        network.LogoutReceived+=Logout;
        _guild=new UiWindow { ToggleKey=Key.G }; root.AddChild(_guild); _guild.Build("Гильдия авантюристов",new(1180,656)); _guild.CloseRequested+=_guild.Close;
        var guildView=GD.Load<PackedScene>("res://UI/Screens/GuildView.tscn").Instantiate<Control>(); _guild.Body.AddChild(guildView);
        _board=guildView.GetNode<VBoxContainer>("%Entries"); _questTitle=guildView.GetNode<Label>("%RecordTitle");
        _questText=guildView.GetNode<Label>("%RecordText"); _rewards=guildView.GetNode<Label>("%Reward");
        guildView.GetNode<Button>("%PrimaryAction").Pressed+=()=>GameUi.Navigate("journal");
        guildView.GetNode<Button>("%SecondaryAction").Pressed+=()=>GameUi.Navigate("map");
        GameUi.RegisterRoute("guild",ToggleGuild); network.QuestJournalReceived+=Quest; Refresh();
        PcInput.Changed+=RefreshBindings;RefreshBindings();
    }
    private void RefreshBindings()
    {
        foreach(var pair in _bindingLabels)
        {
            var title=PcInput.Actions.First(a=>a.Id==pair.Value).Title;
            if(pair.Key.Text.Length!=0)pair.Key.Text=title+" · "+PcInput.Label(pair.Value);
            pair.Key.TooltipText=title+" · "+PcInput.Label(pair.Value);
        }
    }
    private void Notice(string text,string route)
    {
        _notice.Text=text; _noticeRoute=route; _noticeUntil=Time.GetTicksMsec()/1000d+10;
    }
    private void Logout(LogoutResult result)
    {
        _logout.Disabled=result.Outcome==LogoutOutcome.Accepted;
        _logoutStatus.Text=result.Outcome==LogoutOutcome.InCombat?"Выход недоступен: персонаж ещё в бою.":"Персонаж сохранён.";
    }
    private void TradeNotice(TradeState state) { if(state.OwnerId==_owner && state.Phase==TradePhase.Invited) Notice("Приглашение к обмену · B","trade"); }
    private void InviteNotice(SocialInvites state) { if(state.Owner!=_owner) return; foreach(var invite in state.Items) Notice(invite.Kind==SocialKind.Party?"Приглашение в группу · N":"Приглашение в гильдию игроков · O",invite.Kind==SocialKind.Party?"party":"clan"); }
    private void ProfessionNotice(ProfessionState state) { if(state.OwnerId==_owner && state.OfferedId!=0) Notice("Открылась профессия: "+state.OfferedName+" · P","profession"); }
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
    public override void _Process(double delta) { _hud.Visible=!GameUi.GameplayModalOpen; _notice.Visible=_noticeUntil>Time.GetTicksMsec()/1000d; }
    public override void _UnhandledInput(InputEvent ev)
    {
        if(PcInput.Blocked(this)) return;
        if(PcInput.Pressed("menu",ev)) { ToggleMenu(); GetViewport().SetInputAsHandled(); }
        if(PcInput.Pressed("guild",ev)) { ToggleGuild(); GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree() { PcInput.Changed-=RefreshBindings;GameUi.ClearRoutes(); if(_network is not null) { _network.LogoutReceived-=Logout;_network.QuestJournalReceived-=Quest; _network.TradeStateReceived-=TradeNotice; _network.SocialInvitesReceived-=InviteNotice; _network.ProfessionReceived-=ProfessionNotice; } }
}
