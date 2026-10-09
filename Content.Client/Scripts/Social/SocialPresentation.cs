using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
namespace ProjectG.Social;
public partial class SocialPresentation : CanvasLayer
{
    private NetworkClient network=null!; private PlayerController player=null!;
    private PanelContainer panel=null!; private VBoxContainer body=null!; private Label hud=null!,status=null!;
    private ScrollContainer partyHud=null!;
    private readonly Dictionary<SocialKind,SocialRoster> rosters=new();
    private readonly Dictionary<(SocialKind,byte),SocialMember[]> pages=new();
    private readonly Dictionary<ulong,(PartyMemberPresence State,uint Tick,double At)> presence=new();
    private SocialInvites invites; private double clock,sent,refresh; private SocialCommand? pending;
    private SocialKind selected=SocialKind.Party; private ulong operation; private string guildName="";
    public void Initialize(NetworkClient n,PlayerController p)
    {
        network=n;player=p;Layer=9;
        partyHud=new(){Position=new Vector2(24,472),Size=new Vector2(240,168),Visible=false};AddChild(partyHud);
        hud=new(){Text="",MouseFilter=Control.MouseFilterEnum.Ignore};partyHud.AddChild(hud);
        panel=new(){Position=new Vector2(80,80),CustomMinimumSize=new Vector2(520,480),Visible=false};AddChild(panel);
        var outer=new VBoxContainer();panel.AddChild(outer);status=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};outer.AddChild(status);
        var scroll=new ScrollContainer(){CustomMinimumSize=new Vector2(500,440)};outer.AddChild(scroll);body=new(){SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(body);
        n.SocialRosterReceived+=Roster;n.SocialInvitesReceived+=Invites;n.SocialResultReceived+=Result;n.PartyPresenceReceived+=Presence;
    }
    public override void _ExitTree(){if(network is null)return;network.SocialRosterReceived-=Roster;network.SocialInvitesReceived-=Invites;network.SocialResultReceived-=Result;network.PartyPresenceReceived-=Presence;}
    public override void _UnhandledKeyInput(InputEvent e)
    { if(rosters.Count>0 && e is InputEventKey{Pressed:true,Echo:false} k&&k.PhysicalKeycode is Key.N or Key.O) { selected=k.PhysicalKeycode==Key.N?SocialKind.Party:SocialKind.Guild;panel.Visible=!panel.Visible;Render();GetViewport().SetInputAsHandled(); } }
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
    private void Button(string label,Action action){var b=new Button(){Text=label,Disabled=pending is not null};b.Pressed+=action;body.AddChild(b);}
    private void Text(string text)=>body.AddChild(new Label(){Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart});
    private SocialMember[] Members(SocialKind kind)=>pages.Where(p=>p.Key.Item1==kind).OrderBy(p=>p.Key.Item2).SelectMany(p=>p.Value).ToArray();
    private void Render()
    {
        if(body is null)return;foreach(var child in body.GetChildren()){body.RemoveChild(child);child.QueueFree();}
        Text(selected==SocialKind.Party?"Группа — N | Гильдия — O":"Гильдия — O | Группа — N");
        if(!rosters.TryGetValue(selected,out var r)){Text("Ожидаем состояния сервера…");return;}
        if(r.Id==0)
        {
            if(selected==SocialKind.Guild){var edit=new LineEdit(){PlaceholderText="Название гильдии",Text=guildName,MaxLength=96};edit.TextChanged+=v=>guildName=v;body.AddChild(edit);}
            Button(selected==SocialKind.Party?"Создать группу на 6":"Создать гильдию",()=>Request(SocialAction.Create,name:selected==SocialKind.Guild?guildName:""));
        }
        else
        {
            Text((selected==SocialKind.Guild?r.Name:"Группа")+" #"+r.Id+" | "+r.Total+"/"+r.Capacity);
            var members=Members(selected);var own=members.FirstOrDefault(m=>m.Handle==r.Self);var leader=own.Role==SocialRole.Leader;
            foreach(var member in members)
            {
                Text("Игрок #"+member.Handle+" — "+member.Role+(member.Online?" | онлайн":" | офлайн")+(member.Handle==r.Self?" (вы)":""));
                var target=member.Handle;
                if(target==r.Self)continue;
                if(leader||selected==SocialKind.Guild&&own.Role==SocialRole.Officer&&member.Role==SocialRole.Member)Button("Исключить #"+target,()=>Request(SocialAction.Kick,target));
                if(leader){Button("Передать лидерство #"+target,()=>Request(SocialAction.Transfer,target));if(selected==SocialKind.Guild)Button(member.Role==SocialRole.Officer?"Понизить #"+target:"Назначить офицером #"+target,()=>Request(member.Role==SocialRole.Officer?SocialAction.Demote:SocialAction.Promote,target));}
            }
            if(leader&&selected==SocialKind.Party)Button(r.Capacity==6?"Расширить до 20":"Сократить до 6",()=>Request(SocialAction.Resize,capacity:(byte)(r.Capacity==6?20:6)));
            if(selected==SocialKind.Party||!leader)Button("Покинуть",()=>Request(SocialAction.Leave));
            if(selected==SocialKind.Guild&&leader&&r.Total==1){var ack=new CheckBox(){Text="Подтверждаю роспуск гильдии"};body.AddChild(ack);Button("Распустить",()=>{if(ack.ButtonPressed)Request(SocialAction.Disband,acknowledge:true);});}
            if(leader||selected==SocialKind.Guild&&own.Role==SocialRole.Officer)
            {Text("Пригласить видимого игрока:");foreach(var p in network.KnownPlayers.Values.Where(p=>p.EntityId!=player.EntityId).OrderBy(p=>p.EntityId.Value)){var target=p.EntityId.Value;Button("Пригласить игрока (entity #"+target+")",()=>Request(SocialAction.Invite,target));}}
        }
        if(invites.Items is not null)foreach(var i in invites.Items){var invite=i;Text((i.Handoff?"Передача лидерства":"Приглашение")+" от #"+i.From+" в "+i.Kind+" "+i.Name+" (30 с)");Button("Принять",()=>Request(SocialAction.Accept,acknowledge:true,invite:invite));Button("Отклонить",()=>Request(SocialAction.Decline,invite:invite));}
        Text("EXP личный. Лут подбирается вручную. Гильдия не защищает от PvP.");
    }
    public override void _Process(double delta)
    {
        clock+=delta;if(pending is {} c&&clock-sent>=2){sent=clock;network.SendSocial(c);}
        if(clock<refresh)return;refresh=clock+.5;
        hud.Text="Группа · N"; partyHud.Visible=rosters.TryGetValue(SocialKind.Party,out var party)&&party.Id!=0;
        if(rosters.TryGetValue(SocialKind.Party,out var r)&&r.Id!=0)
        {foreach(var m in Members(SocialKind.Party)){var text="#"+m.Handle+": ";if(presence.TryGetValue(m.Handle,out var p)&&clock-p.At<3)text+=(p.State.Flags&1)==0?((p.State.Flags&4)!=0?"отключён в бою":"офлайн"):(p.State.Flags&2)!=0?"погиб":p.State.Health.ToString("0")+"/"+p.State.MaxHealth.ToString("0")+" | "+p.State.Position.X.ToString("0")+", "+p.State.Position.Y.ToString("0");else text+="нет свежих данных";hud.Text+="\n"+text;}}
        var size=GetViewport().GetVisibleRect().Size;panel.Position=new Vector2(Math.Max(12,size.X-550),12);
    }
}
