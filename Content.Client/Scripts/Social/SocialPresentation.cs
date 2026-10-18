using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Social;
public partial class SocialPresentation : CanvasLayer
{
    private NetworkClient network=null!; private PlayerController player=null!;
    private UiWindow panel=null!; private Label hud=null!,status=null!,rosterTitle=null!;
    private VBoxContainer memberRows=null!,inviteRows=null!,nearbyRows=null!;
    private Control create=null!,disband=null!;
    private Button createGroup=null!,resize=null!,leave=null!,disbandGroup=null!;
    private LineEdit guildNameEdit=null!;
    private CheckBox acknowledge=null!;
    private ScrollContainer partyHud=null!;
    private readonly Dictionary<SocialKind,SocialRoster> rosters=new();
    private readonly Dictionary<(SocialKind,byte),SocialMember[]> pages=new();
    private readonly Dictionary<ulong,(PartyMemberPresence State,uint Tick,double At)> presence=new();
    private SocialInvites invites; private double clock,sent,refresh; private SocialCommand? pending;
    private SocialKind selected=SocialKind.Party; private ulong operation; private string guildName="";
    public void Initialize(NetworkClient n,PlayerController p)
    {
        network=n;player=p;Layer=9;
        var root=new Control { Theme=GameUi.CreateTheme(),MouseFilter=Control.MouseFilterEnum.Ignore }; AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        partyHud=GD.Load<PackedScene>("res://UI/Components/PartyHud.tscn").Instantiate<ScrollContainer>(); root.AddChild(partyHud);
        hud=partyHud.GetNode<Label>("%Members");
        panel=new UiWindow(); root.AddChild(panel); panel.Build("Группа и гильдия игроков",new(1180,656)); panel.CloseRequested+=panel.Close;
        var view=GD.Load<PackedScene>("res://UI/Screens/SocialView.tscn").Instantiate<Control>(); panel.Body.AddChild(view);
        view.GetNode<Button>("%Party").Pressed+=()=> { selected=SocialKind.Party;Render(); };
        view.GetNode<Button>("%Clan").Pressed+=()=> { selected=SocialKind.Guild;Render(); };
        status=view.GetNode<Label>("%Status"); rosterTitle=view.GetNode<Label>("%RosterTitle");
        memberRows=view.GetNode<VBoxContainer>("%MembersRows"); inviteRows=view.GetNode<VBoxContainer>("%InvitesRows"); nearbyRows=view.GetNode<VBoxContainer>("%NearbyRows");
        create=view.GetNode<Control>("%Create"); disband=view.GetNode<Control>("%Disband"); guildNameEdit=view.GetNode<LineEdit>("%GuildName");
        createGroup=view.GetNode<Button>("%CreateGroup"); resize=view.GetNode<Button>("%Resize"); leave=view.GetNode<Button>("%Leave"); disbandGroup=view.GetNode<Button>("%DisbandGroup"); acknowledge=view.GetNode<CheckBox>("%Acknowledge");
        guildNameEdit.TextChanged+=value=>guildName=value;
        createGroup.Pressed+=()=>Request(SocialAction.Create,name:selected==SocialKind.Guild?guildName:"");
        resize.Pressed+=()=> { if(rosters.TryGetValue(selected,out var roster))Request(SocialAction.Resize,capacity:(byte)(roster.Capacity==6?20:6)); };
        leave.Pressed+=()=>Request(SocialAction.Leave);
        acknowledge.Toggled+=on=>disbandGroup.Disabled=pending is not null || !on;
        disbandGroup.Pressed+=()=> { if(acknowledge.ButtonPressed)Request(SocialAction.Disband,acknowledge:true); };
        Render();
        n.SocialRosterReceived+=Roster;n.SocialInvitesReceived+=Invites;n.SocialResultReceived+=Result;n.PartyPresenceReceived+=Presence;
    }
    public override void _ExitTree(){if(network is null)return;network.SocialRosterReceived-=Roster;network.SocialInvitesReceived-=Invites;network.SocialResultReceived-=Result;network.PartyPresenceReceived-=Presence;}
    public override void _UnhandledInput(InputEvent e)
    { if(rosters.Count>0 && !PcInput.Blocked(this) && (PcInput.Pressed("party",e)||PcInput.Pressed("clan",e))) { Toggle(PcInput.Pressed("party",e)?SocialKind.Party:SocialKind.Guild);GetViewport().SetInputAsHandled(); } }
    public void Toggle(SocialKind kind) { selected=kind; panel.ToggleKey=kind==SocialKind.Party?Key.N:Key.O; if(panel.Visible) panel.Close(); else { Render(); panel.Open(); } }
    private void Roster(SocialRoster r)
    {
        if(r.Owner!=player.EntityId)return;
        if(rosters.TryGetValue(r.Kind,out var old)&&r.Tick<old.Tick)return;
        if(!rosters.TryGetValue(r.Kind,out old)||old.Id!=r.Id||old.Revision!=r.Revision)
        { foreach(var key in pages.Keys.Where(k=>k.Item1==r.Kind).ToArray())pages.Remove(key); if(r.Kind==SocialKind.Party)presence.Clear(); }
        rosters[r.Kind]=r;pages[(r.Kind,r.Page)]=r.Members;operation=Math.Max(operation,r.LastOperation);Render();
    }
    private void Invites(SocialInvites i){if(i.Owner!=player.EntityId)return;invites=i;Render();}
    private void Result(SocialResult r){if(pending?.Operation!=r.Operation)return;pending=null;status.Text=r.Outcome switch{SocialOutcome.Accepted=>"Сохранено.",SocialOutcome.Busy=>"Дождитесь окончания PvP-тега, агрессии, каста и эффектов.",SocialOutcome.Full=>"Нет свободных мест или достигнут лимит.",SocialOutcome.Stale=>"Состав изменился. Повторите действие.",SocialOutcome.NotAllowed=>"Недостаточно прав для этого действия.",SocialOutcome.InvalidName=>"Имя: 3–24 буквы, цифры, пробелы или дефис. Имя должно быть свободно.",SocialOutcome.Missing=>"Приглашение истекло или игрок недоступен.",_=>"Действие отклонено: "+r.Outcome};Render();}
    private void Presence(PartyPresence p)
    {
        if(p.Owner!=player.EntityId||!rosters.TryGetValue(SocialKind.Party,out var r)||r.Id!=p.Party||r.Revision!=p.Revision)return;
        foreach(var m in p.Members)if(!presence.TryGetValue(m.Handle,out var old)||Content.Shared.Movement.MovementSimulation.IsSequenceNewer(p.Tick,old.Tick))presence[m.Handle]=(m,p.Tick,clock);
    }
    private void Request(SocialAction action,ulong target=0,byte capacity=0,bool acknowledge=false,string name="",SocialInvite? invite=null)
    {
        if(pending is not null||!rosters.TryGetValue(selected,out var r))return;
        var c=new SocialCommand(++operation,invite?.Kind??selected,action,target,action==SocialAction.Create?0:invite?.Revision??r.Revision,invite?.Token??0,capacity,acknowledge,name);
        pending=c;sent=clock;status.Text="Ожидаем сохранения…";network.SendSocial(c);Render();
    }
    private SocialMember[] Members(SocialKind kind)=>pages.Where(p=>p.Key.Item1==kind).OrderBy(p=>p.Key.Item2).SelectMany(p=>p.Value).ToArray();
    private static string RoleName(SocialRole role)=>role switch {SocialRole.Leader=>"Лидер",SocialRole.Officer=>"Офицер",_=>"Участник"};
    private void Render()
    {
        if(memberRows is null)return;
        foreach(var list in new[]{memberRows,inviteRows,nearbyRows}) UiRows.Clear(list);
        panel.Title.Text=selected==SocialKind.Party?"Группа":"Гильдия игроков";
        var known=rosters.TryGetValue(selected,out var r); var exists=known&&r.Id!=0;
        var members=Members(selected); var own=members.FirstOrDefault(m=>m.Handle==r.Self); var leader=exists&&own.Role==SocialRole.Leader;
        create.Visible=known&&!exists; guildNameEdit.Visible=selected==SocialKind.Guild;
        createGroup.Text=selected==SocialKind.Party?"Создать группу на 6":"Создать гильдию"; createGroup.Disabled=pending is not null;
        resize.Visible=leader&&selected==SocialKind.Party; resize.Text=r.Capacity==6?"Расширить до 20":"Сократить до 6"; resize.Disabled=pending is not null;
        leave.Visible=exists&&(selected==SocialKind.Party||!leader); leave.Disabled=pending is not null;
        disband.Visible=leader&&selected==SocialKind.Guild&&r.Total==1; disbandGroup.Disabled=pending is not null||!acknowledge.ButtonPressed;
        rosterTitle.Text=!known?"Состав загружается…":!exists?(selected==SocialKind.Party?"Вы пока не в группе":"Вы пока не в гильдии"):(selected==SocialKind.Guild?r.Name:"Группа")+$" · {r.Total}/{r.Capacity}";
        if(!exists) UiRows.Note(memberRows,known?"Создайте объединение или примите приглашение.":"Ожидаем состояния сервера…");
        foreach(var member in exists?members:[])
        {
            var target=member.Handle;
            var canKick=target!=r.Self&&(leader||selected==SocialKind.Guild&&own.Role==SocialRole.Officer&&member.Role==SocialRole.Member);
            var row=UiRows.Action(memberRows,"Игрок #"+target+(target==r.Self?" · вы":""),RoleName(member.Role)+(member.Online?" · в сети":" · не в сети"),"nav.character",canKick?"Исключить":"",canKick?()=>Request(SocialAction.Kick,target):null,pending is not null);
            if(target==r.Self||!leader)continue;
            UiRows.Secondary(row,"Передать лидерство",()=>Request(SocialAction.Transfer,target),pending is not null);
            if(selected==SocialKind.Guild) UiRows.Button(row.GetNode<VBoxContainer>("%Actions"),member.Role==SocialRole.Officer?"Понизить":"Назначить офицером",()=>Request(member.Role==SocialRole.Officer?SocialAction.Demote:SocialAction.Promote,target),pending is not null);
        }
        if(exists&&(leader||selected==SocialKind.Guild&&own.Role==SocialRole.Officer))
        {
            foreach(var playerInfo in network.KnownPlayers.Values.Where(value=>value.EntityId!=player.EntityId).OrderBy(value=>value.EntityId.Value))
            { var target=playerInfo.EntityId.Value; UiRows.Action(nearbyRows,"Игрок "+playerInfo.PlayerId.Value,"Видимый игрок","nav.character","Пригласить",()=>Request(SocialAction.Invite,target),pending is not null); }
        }
        if(nearbyRows.GetChildCount()==0) UiRows.Note(nearbyRows,exists?"Нет доступных игроков или прав на приглашение.":"Сначала создайте объединение.");
        foreach(var entry in invites.Items??[])
        {
            var invite=entry;
            var row=UiRows.Action(inviteRows,entry.Handoff?"Передача лидерства":"Приглашение",entry.Name+" · от #"+entry.From+" · 30 с","icon.guild","Принять",()=>Request(SocialAction.Accept,acknowledge:true,invite:invite),pending is not null);
            UiRows.Secondary(row,"Отклонить",()=>Request(SocialAction.Decline,invite:invite),pending is not null);
        }
        if(inviteRows.GetChildCount()==0) UiRows.Note(inviteRows,"Приглашений пока нет.");
    }
    public override void _Process(double delta)
    {
        clock+=delta;if(pending is {} c&&clock-sent>=2){sent=clock;network.SendSocial(c);}
        if(clock<refresh)return;refresh=clock+.5;
        hud.Text="Группа · N"; partyHud.Visible=!GameUi.GameplayModalOpen && rosters.TryGetValue(SocialKind.Party,out var party)&&party.Id!=0;
        if(rosters.TryGetValue(SocialKind.Party,out var r)&&r.Id!=0)
        {foreach(var m in Members(SocialKind.Party)){var text="#"+m.Handle+": ";if(presence.TryGetValue(m.Handle,out var p)&&clock-p.At<3)text+=(p.State.Flags&1)==0?((p.State.Flags&4)!=0?"отключён в бою":"офлайн"):(p.State.Flags&2)!=0?"погиб":p.State.Health.ToString("0")+"/"+p.State.MaxHealth.ToString("0");else text+="нет свежих данных";hud.Text+="\n"+text;}}

    }
}
