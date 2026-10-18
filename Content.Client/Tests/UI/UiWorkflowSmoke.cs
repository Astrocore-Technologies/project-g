using Content.Shared.Network;
using Godot;
using ProjectG.Gameplay;
using ProjectG.Networking;
using ProjectG.UI;
namespace ProjectG.Tests.UI;

/// <summary>Actual scene controls and authoritative replies, using two isolated review identities.</summary>
public partial class UiWorkflowSmoke : Node
{
    private WorldController _world=null!;
    private PlayerController _player=null!;
    public override async void _Ready()
    {
        try
        {
            GetWindow().Mode=Window.ModeEnum.Windowed;GetWindow().Size=new(1280,720);
            using var config=System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://../.artifacts/ui-redesign/live.json"));
            var port=config.RootElement.GetProperty("port").GetInt32();var suffix=Guid.NewGuid().ToString("N")[..8];
            _world=GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<WorldController>();_world.AutoConnect=false;
            _world.UiSettingsPath="res://../.artifacts/ui-final-2026-10-18/review-settings.cfg";
            var network=_world.GetNode<NetworkClient>("NetworkClient");network.DevelopmentIdentityProfile="ui-flow-"+suffix;
            CraftState? craft=null;CraftResult? craftResult=null;EconomyQuote? quote=null;EconomyResult? economyResult=null;MarketState? market=null;
            TradeState? trade=null;TradeState? peerTrade=null;TradeResult? tradeReply=null;SocialRoster? roster=null;SocialInvites? peerInvites=null;
            network.CraftStateReceived+=v=>craft=v;network.CraftResultReceived+=v=>craftResult=v;network.EconomyQuoteReceived+=v=>quote=v;
            network.EconomyResultReceived+=v=>economyResult=v;network.MarketStateReceived+=v=>market=v;network.TradeStateReceived+=v=>trade=v;
            network.SocialRosterReceived+=v=>{if(v.Kind==SocialKind.Party)roster=v;};
            network.TradeResultReceived+=v=>{tradeReply=v;GD.Print("UI_TRADE_REPLY: "+v.Outcome);};
            AddChild(_world);network.ConnectToServerAt(port);await Wait(()=>network.RegionActive&&craft is not null,"owner login");await Frames(10);
            _player=_world.GetChildren().OfType<PlayerController>().Single(p=>p.GetNode<Camera3D>("CameraRig/Camera3D").Current);

            GameUi.Navigate("craft");await Frames(5);
            await Click(VisibleButton("Собрать"));await Wait(()=>craftResult is not null,"out-of-range gather reply");
            if(craftResult!.Value.Outcome!=CraftOutcome.TooFar)throw new Exception("Gather distance rejection was lost.");
            await Capture("craft-distance-error");Close();await Frames(3);
            var wood=network.ResourceNodes.Values.Single(v=>v.MaterialId==1);await Walk(wood.Position);
            craftResult=null;GameUi.Navigate("craft");await Frames(4);await Click(VisibleButton("Собрать"));
            await Wait(()=>craftResult is {Outcome:CraftOutcome.Accepted}&&craft!.Value.Materials.Any(m=>m.Id==1&&m.Quantity>0),"gather saved");
            await Capture("craft-gathered");Close();await Frames(3);

            await Walk(network.CraftRecipes.Values.First().StationPosition);
            GameUi.Navigate("market");await Frames(4);var marketTabs=VisibleWindow().FindChildren("*","TabContainer",true,false).OfType<TabContainer>().Single();marketTabs.CurrentTab=1;await Frames(3);
            await Click(VisibleButton("Выставить"));await Wait(()=>quote is not null,"listing quote");await Capture("market-quote");
            await Click(Named<Button>("Cancel"));await Frames(3);
            if(!VisibleButton("Выставить").IsVisibleInTree())throw new Exception("Cancel did not return to market.");
            quote=null;await Click(VisibleButton("Выставить"));await Wait(()=>quote is not null,"new listing quote");
            await Click(Named<Button>("Confirm"));await Wait(()=>economyResult is {Outcome:CraftOutcome.Accepted}&&market is {} m&&m.Listings.Any(l=>l.Own),"listing saved");
            await Capture("market-listing");await Click(VisibleButton("Снять"));await Wait(()=>Named<Control>("Confirmation").Visible,"return quote");
            economyResult=null;await Click(Named<Button>("Confirm"));await Wait(()=>economyResult is {Effect:EconomyEffect.Cancelled},"listing returned");Close();await Frames(3);

            await Walk(new(-9,0));
            var peer=new NetworkClient{DevelopmentIdentityProfile="ui-peer-"+suffix};peer.NavigationReceived+=_=>peer.ConfirmRegionScene();
            peer.TradeStateReceived+=v=>peerTrade=v;peer.SocialInvitesReceived+=v=>peerInvites=v;AddChild(peer);peer.ConnectToServerAt(port);
            await Wait(()=>peer.CurrentRegion is not null&&peer.RegionActive&&peer.LatestExploration is not null&&peer.LocalPlayerId.IsValid&&network.KnownPlayers.Values.Any(p=>p.PlayerId==peer.LocalPlayerId),"peer login");await Frames(20);
            var peerSpawn=network.KnownPlayers.Values.Single(p=>p.PlayerId==peer.LocalPlayerId);await Walk(peerSpawn.Position+new System.Numerics.Vector2(1,0));
            GD.Print("UI_TRADE_READY: owner="+_player.PredictedPosition+" peer="+peerSpawn.Position+" active="+network.RegionActive+"/"+peer.RegionActive);
            GameUi.Navigate("trade");await Frames(5);await Click(VisibleButton("Пригласить"));
            var tradeUi=_world.GetChildren().OfType<ProjectG.Trading.TradePresentation>().Single();
            GD.Print("UI_TRADE_SEQUENCE: "+typeof(ProjectG.Trading.TradePresentation).GetField("_sequence",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(tradeUi));
            await Wait(()=>peerTrade is {Phase:TradePhase.Invited}||tradeReply is {} r&&r.Outcome!=CraftOutcome.Accepted,"trade invitation");
            if(tradeReply is {} rejected&&rejected.Outcome!=CraftOutcome.Accepted)throw new Exception("Trade invite rejected: "+rejected.Outcome);
            await Capture("trade-invitation");var invitation=peerTrade!.Value;
            peer.SendTrade(new(1,TradeAction.Accept,invitation.SessionId,invitation.PartnerId,invitation.Revision,[],[]));
            await Wait(()=>trade is {Phase:TradePhase.Negotiating},"trade negotiating");await Frames(3);
            var selection=VisibleWindow().FindChildren("*","CheckBox",true,false).OfType<CheckBox>().First(b=>b.IsVisibleInTree());await Click(selection);
            await Click(Named<Button>("Offer"));await Wait(()=>trade!.Value.OwnItems.Count==1,"offer acknowledged");await Capture("trade-offer");
            await Click(Named<Button>("Accept"));await Wait(()=>peerTrade is {PartnerAccepted:true},"first acceptance");
            var final=peerTrade!.Value;peer.SendTrade(new(2,TradeAction.Accept,final.SessionId,final.PartnerId,final.Revision,[],[]));
            await Wait(()=>trade is {Phase:TradePhase.Completed},"trade completed");await Capture("trade-completed");Close();await Frames(3);

            GameUi.Navigate("party");await Frames(3);await Click(Named<Button>("CreateGroup"));
            await Wait(()=>roster is {Id:>0},"party created");await Click(Named<Button>("Resize"));await Wait(()=>roster is {Capacity:20},"party resize");
            await Click(VisibleButton("Пригласить"));await Wait(()=>peerInvites is {} i&&i.Items.Length>0,"social invitation");
            var invite=peerInvites!.Value.Items.Single();peer.SendSocial(new(1,SocialKind.Party,SocialAction.Accept,0,invite.Revision,invite.Token,0,true,""));
            await Wait(()=>roster is {Total:2},"second party member");await Capture("party-two-members");
            await Click(Named<Button>("Leave"));await Wait(()=>roster is {Id:0},"party leave");Close();
            peer.QueueFree();_world.QueueFree();await Frames(5);
            if(GameUi.GameplayModalOpen)throw new Exception("Workflow leaked modal state.");
            UiReviewEvidence.Record("workflows","PASS: distance rejection, gather, quote cancellation, listing/return, two-client item trade, party create/resize/invite/accept/leave");
            GD.Print("UI_WORKFLOW_OK: distance rejection, real gather, market quote/cancel/list/return, two-client trade with item, party create/resize/invite/accept/leave.");GetTree().Quit();
        }
        catch(Exception e){UiReviewEvidence.Record("workflows","FAIL: "+e);GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private UiWindow VisibleWindow()=>Descendants(_world).OfType<UiWindow>().Single(w=>w.Visible);
    private T Named<T>(string name) where T:Node=>Descendants(VisibleWindow()).OfType<T>().Single(n=>n.Name==name);
    private Button VisibleButton(string text)=>Descendants(VisibleWindow()).OfType<Button>().First(b=>b.IsVisibleInTree()&&b.Text==text);
    private static IEnumerable<Node> Descendants(Node n){foreach(var child in n.GetChildren()){yield return child;foreach(var next in Descendants(child))yield return next;}}
    private async Task Walk(System.Numerics.Vector2 p){if(!_player.MoveTo(p))throw new Exception("Review path blocked.");await Wait(()=>System.Numerics.Vector2.Distance(_player.PredictedPosition,p)<.2,"approach");await Frames(12);}
    private static void Close(){Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=Key.Escape,Keycode=Key.Escape,Pressed=true});Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=Key.Escape,Keycode=Key.Escape,Pressed=false});}
    private async Task Click(BaseButton b){if(b.Disabled)throw new Exception("Disabled action: "+b.Name);var p=b.GetGlobalRect().GetCenter();Input.WarpMouse(p);await Frames(2);Input.ParseInputEvent(new InputEventMouseButton{Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);Input.ParseInputEvent(new InputEventMouseButton{Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=false});await Frames(3);}
    private async Task Frames(int n){for(var i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Wait(Func<bool> c,string name){var end=Time.GetTicksMsec()+20000;while(!c()&&Time.GetTicksMsec()<end)await Frames(1);if(!c())throw new TimeoutException(name);}
    private async Task Capture(string name){DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://../.artifacts/ui-final-2026-10-18/workflows"));await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng("res://../.artifacts/ui-final-2026-10-18/workflows/"+name+".png");}
}
