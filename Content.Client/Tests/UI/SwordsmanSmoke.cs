using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.Progression;
using ProjectG.Quests;
using ProjectG.Items;
using ProjectG.UI;

namespace ProjectG.Tests.UI;

/// <summary>Playable training loop using real GUI/input, then a durable reconnect on an isolated profile.</summary>
public partial class SwordsmanSmoke : Node
{
    private ProfessionState _profession;
    private ProgressionState _progression;
    private AbilityLoadout _loadout;
    private bool _animatedBasic, _animatedThrust;
    private readonly Dictionary<NetworkEntityId,CombatState> _combat=new();
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed; GetWindow().Size=new(1280,720);
            var world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); world.AutoConnect=false; AddChild(world);
            var network=world.GetNode<NetworkClient>("NetworkClient"); Attach(network);
            QuestReply? reply=null; network.QuestReplyReceived+=r=>reply=r;
            network.ConnectToServer(); await Wait(()=>_profession.OwnerId.IsValid,"login");
            var player=world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);
            await Walk(player,new(15,-7));
            var dialogue=world.GetChildren().OfType<QuestPresentation>().Single();
            Press(Key.F2); await Wait(()=>reply is {Choices:4},"trainer offer");
            await Capture("swordsman-trainer");
            await ClickText(dialogue,"Получить тренировочный меч"); await Wait(()=>_profession.TrainingRequired==100,"training accepted");
            Press(Key.Escape); Press(Key.I); await Delay(.3);
            var inventory=world.GetChildren().OfType<InventoryPresentation>().Single();
            var item=Descendants(inventory).OfType<UiIconSlot>().Single(b=>!b.Disabled && b.TooltipText.Contains("Тренировочный меч"));
            await Click(item); await ClickText(inventory,"Надеть"); await Delay(.5); Press(Key.Escape);
            await Walk(player,new(15,-12.5f)); await Capture("swordsman-arena");
            var dummy=_combat.Values.Single(s=>s.Kind==CombatEntityKind.TrainingTarget && System.Numerics.Vector2.Distance(s.Position,new(15,-14))<.1);
            var point=GetViewport().GetCamera3D().UnprojectPosition(new(dummy.Position.X,1.5f,dummy.Position.Y));
            await MouseClick(point);
            await Wait(()=>_profession.TrainingDamage==100,"100 actual training damage");
            if (!_animatedBasic) throw new Exception("Ordinary attacks did not animate the avatar.");
            player.GetNode<ProjectG.Combat.CombatPresentation>("CombatPresentation").CancelAutoAttack();
            if(_profession.OfferedId!=0) throw new Exception("Profession was offered before returning to trainer.");
            await Walk(player,new(15,-7)); reply=null; Press(Key.F2); await Wait(()=>reply is {Choices:8},"trainer graduation");
            await ClickText(dialogue,"Рассмотреть профессию Мечника");
            var professionUi=world.GetChildren().OfType<ProfessionPresentation>().Single();
            await ClickText(professionUi,"Рассмотреть переход");
            await Wait(()=>Descendants(professionUi).OfType<CheckBox>().Any(),"server profession preparation");
            await Click(Descendants(professionUi).OfType<CheckBox>().Single());
            await ClickText(professionUi,"Подтвердить необратимый переход");
            await Wait(()=>_profession.ActiveId==2 && _progression.Skills.Count(s=>s.Id is >=20 and <=29)==10,"profession learned");
            if(_loadout.Abilities.Count!=9 || _loadout.Abilities.Single(a=>a.Form==AbilityForm.Dash).Range!=20) throw new Exception("Sword loadout/dash missing.");
            await Capture("swordsman-profession"); Press(Key.Escape); Press(Key.K); await Delay(.2);
            var tabs=Descendants(world).OfType<TabContainer>().Single(t=>t.GetChildren().Any(c=>c.Name=="Навыки")); tabs.CurrentTab=1;
            await Capture("swordsman-skills"); Press(Key.Escape);
            await Walk(player,new(15,-12.5f)); var hp=_combat[dummy.EntityId].Health;
            Input.WarpMouse(GetViewport().GetCamera3D().UnprojectPosition(new(15,0,-15))); await Delay(.15); Press(Key.Q);
            await Wait(()=>_combat[dummy.EntityId].Health<hp && _animatedThrust,"actual animated sword skill hit");
            await Capture("swordsman-hud");
            world.QueueFree(); await Delay(.6); _profession=default; _progression=default;
            var restored=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>(); restored.AutoConnect=false; AddChild(restored);
            var next=restored.GetNode<NetworkClient>("NetworkClient"); Attach(next); next.ConnectToServer();
            await Wait(()=>_profession.ActiveId==2 && _progression.Skills.Count(s=>s.Id is >=20 and <=29)==10,"saved swordsman reconnect");
            restored.QueueFree(); await Delay(.3);
            if(GameUi.GameplayModalOpen) throw new Exception("Modal leaked after cleanup.");
            GD.Print("SWORDSMAN_SMOKE_OK: trainer GUI, equip, real autoattack training, voluntary confirmation, ten skills, long dash, skill hit, durable reconnect."); GetTree().Quit();
        }
        catch(Exception error) { await Capture("swordsman-failure"); GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void Attach(NetworkClient network)
    {
        network.ProfessionReceived+=v=>_profession=v; network.ProgressionReceived+=v=>_progression=v;
        network.AbilityLoadoutReceived+=v=>_loadout=v; network.CombatStateReceived+=v=>_combat[v.EntityId]=v;
        network.AttackReceived+=v=>
        {
            var actor=network.GetParent().GetChildren().OfType<PlayerController>().FirstOrDefault(p=>p.EntityId==v.AttackerId);
            if(actor?.SwordAnimation.IsAnimating==true && actor.SwordAnimation.Clip.ToString().StartsWith("basic_")) _animatedBasic=true;
        };
        network.AbilityEffectReceived+=v=>
        {
            var actor=network.GetParent().GetChildren().OfType<PlayerController>().FirstOrDefault(p=>p.EntityId==v.ActorId);
            if(v.AbilityId==20 && v.Phase==AbilityPhase.Impact && actor?.SwordAnimation.IsAnimating==true && actor.SwordAnimation.Clip=="thrust") _animatedThrust=true;
        };
        network.AbilityHitReceived+=v=> { if(_combat.TryGetValue(v.TargetId,out var c)) _combat[v.TargetId]=c with {Health=v.TargetHealth}; };
    }
    private async Task Walk(PlayerController p,System.Numerics.Vector2 destination)
    { if(!p.MoveTo(destination)) throw new Exception("Arena path unavailable."); await Wait(()=>System.Numerics.Vector2.Distance(p.PredictedPosition,destination)<.15,"walk"); await Delay(.4); }
    private static IEnumerable<Node> Descendants(Node n) { foreach(var c in n.GetChildren()) { yield return c; foreach(var v in Descendants(c)) yield return v; } }
    private async Task ClickText(Node root,string text) => await Click(Descendants(root).OfType<Button>().Single(b=>b.Text==text));
    private async Task Click(Button b) { await Wait(()=>b.IsVisibleInTree() && !b.Disabled,"button: "+b.Text); await MouseClick(b.GetGlobalRect().GetCenter()); await Delay(.15); }
    private async Task MouseClick(Vector2 p)
    {
        Input.WarpMouse(p); await Delay(.05);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=p,GlobalPosition=p}); await Delay(.05);
        Input.ParseInputEvent(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=p,GlobalPosition=p});
    }
    private static void Press(Key k) { Input.ParseInputEvent(new InputEventKey {PhysicalKeycode=k,Keycode=k,Pressed=true}); Input.ParseInputEvent(new InputEventKey {PhysicalKeycode=k,Keycode=k,Pressed=false}); }
    private async Task Delay(double seconds)=>await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private async Task Wait(Func<bool> done,string stage)
    { var until=Time.GetTicksMsec()+30000; while(!done()) { if(Time.GetTicksMsec()>until) throw new TimeoutException(stage); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); } }
    private async Task Capture(string name)
    { if(!OS.GetCmdlineUserArgs().Contains("--capture"))return; await Delay(.25); await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/"+name+".png"); }
}
