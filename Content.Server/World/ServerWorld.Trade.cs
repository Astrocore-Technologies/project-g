using System.Numerics;
using Content.Server.Items;
using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.World;
public sealed partial class ServerWorld
{
    private sealed class Trade(ulong id,NetworkEntityId first,NetworkEntityId second,double expires)
    {
        public ulong Id=id; public NetworkEntityId First=first,Second=second; public double Expires=expires; public uint Revision=1; public TradePhase Phase=TradePhase.Invited;
        public ExchangeOffer A=ExchangeOffer.Empty,B=ExchangeOffer.Empty; public bool AcceptedA,AcceptedB;
        public TradeItem[] PreviewA=[],PreviewB=[];
    }
    private readonly Dictionary<ulong,Trade> _trades=new();
    private readonly Dictionary<NetworkEntityId,ulong> _playerTrades=new();
    private readonly Dictionary<int,TradeCommand> _tradePending=new();
    private readonly Dictionary<int,(uint Sequence,uint Tick)> _tradeRequests=new();
    private readonly Dictionary<NetworkEntityId,TradeState> _tradeStates=new();
    private readonly Dictionary<NetworkEntityId,TradeResult> _tradeResults=new();
    public IReadOnlyDictionary<NetworkEntityId,TradeState> TradeStates => _tradeStates;
    public IReadOnlyDictionary<NetworkEntityId,TradeResult> TradeResults => _tradeResults;
    public bool TryQueueTrade(int connection,TradeCommand command)
    {
        if(!HasCrafting || !_playersByConnection.TryGetValue(connection,out var player) || command.Sequence==0 || !Enum.IsDefined(command.Action) || !command.PartnerId.IsValid || command.Items is null || command.Materials is null || command.Items.Count>4 || command.Materials.Count>4) return false;
        if(_tradeRequests.TryGetValue(connection,out var request)) { if(!MovementSimulation.IsSequenceNewer(command.Sequence,request.Sequence)) return false; if(request.Tick==Tick) { _tradePending.Remove(connection); _tradeResults[player.EntityId]=new(command.Sequence,Tick+1,CraftOutcome.RateLimited); return false; } }
        _tradeRequests[connection]=(command.Sequence,Tick); _tradePending[connection]=command; return true;
    }
    private bool TradeReady(NetworkEntityId id)
    { if(!_playersByEntity.TryGetValue(id,out var player)) return false; var actor=Combat!.Get(id); return actor.Health>0 && !actor.IsCasting && !player.Motion.IsMoving && !player.Motion.IsDashing && !Abilities!.HasActiveEffects(id); }
    private bool TradeNear(NetworkEntityId a,NetworkEntityId b) => TradeReady(a) && TradeReady(b) && Vector3.DistanceSquared(_playersByEntity[a].Foot,_playersByEntity[b].Foot)<=9 && Navigation.ClearAttack(_playersByEntity[a].Foot,_playersByEntity[b].Foot);
    private TradeItem[] TradePreview(NetworkEntityId id,ExchangeOffer offer)
    { var conditions=Inventory!.ConditionState(id,Tick).Items.ToDictionary(i=>i.Handle); return Inventory.OfferPreview(id,offer).Select(i=> { conditions.TryGetValue(i.Handle,out var c); return new TradeItem(i.Handle,i.Name,i.Slot,i.AttackBonus,i.DefenseBonus,i.HealthBonus,c.Current,c.Maximum); }).ToArray(); }
    private void PublishTrade(Trade trade)
    {
        if(trade.Phase is TradePhase.Invited or TradePhase.Negotiating) { trade.PreviewA=TradePreview(trade.First,trade.A); trade.PreviewB=TradePreview(trade.Second,trade.B); }
        _tradeStates[trade.First]=new(trade.First,Tick,trade.Id,trade.Second,trade.Revision,trade.Phase,trade.AcceptedA,trade.AcceptedB,trade.PreviewA,trade.A.Materials,trade.PreviewB,trade.B.Materials);
        _tradeStates[trade.Second]=new(trade.Second,Tick,trade.Id,trade.First,trade.Revision,trade.Phase,trade.AcceptedB,trade.AcceptedA,trade.PreviewB,trade.B.Materials,trade.PreviewA,trade.A.Materials);
    }
    private void CloseTrade(Trade trade,TradePhase phase)
    { trade.Phase=phase; PublishTrade(trade); Inventory!.ReleaseExchange(trade.First,trade.Id); Inventory.ReleaseExchange(trade.Second,trade.Id); _playerTrades.Remove(trade.First); _playerTrades.Remove(trade.Second); _trades.Remove(trade.Id); }
    private void SimulateTrade()
    {
        foreach(var trade in _trades.Values.ToArray()) if(trade.Expires<=Combat!.Time || !TradeNear(trade.First,trade.Second)) CloseTrade(trade,TradePhase.Cancelled);
        foreach(var (connection,command) in _tradePending)
        {
            var player=_playersByConnection[connection]; var id=player.EntityId; var outcome=CraftOutcome.Accepted;
            if(command.Action==TradeAction.Invite)
            {
                if(command.SessionId!=0 || command.Items.Count!=0 || command.Materials.Count!=0 || id==command.PartnerId || _playerTrades.ContainsKey(id) || _playerTrades.ContainsKey(command.PartnerId)) outcome=CraftOutcome.Busy;
                else if(!TradeNear(id,command.PartnerId)) outcome=CraftOutcome.TooFar;
                else if(_trades.Count>=32) outcome=CraftOutcome.RateLimited;
                else
                {
                    ulong key; do key=(ulong)Random.Shared.NextInt64(1,long.MaxValue); while(_trades.ContainsKey(key));
                    if(!Inventory!.HoldExchange(id,key)) { _tradeResults[id]=new(command.Sequence,Tick,CraftOutcome.Busy); continue; }
                    if(!Inventory.HoldExchange(command.PartnerId,key)) { Inventory.ReleaseExchange(id,key); _tradeResults[id]=new(command.Sequence,Tick,CraftOutcome.Busy); continue; }
                    var trade=new Trade(key,id,command.PartnerId,Combat!.Time+60); _trades.Add(key,trade); _playerTrades[id]=key; _playerTrades[command.PartnerId]=key; PublishTrade(trade);
                }
            }
            else if(!_playerTrades.TryGetValue(id,out var session) || session!=command.SessionId || !_trades.TryGetValue(session,out var current) || (id==current.First ? current.Second : current.First)!=command.PartnerId) outcome=CraftOutcome.Unavailable;
            else if(command.Action==TradeAction.Cancel) CloseTrade(current,TradePhase.Cancelled);
            else if(command.Revision!=current.Revision) outcome=CraftOutcome.InvalidOperation;
            else if(!TradeNear(current.First,current.Second)) { CloseTrade(current,TradePhase.Cancelled); outcome=CraftOutcome.InvalidState; }
            else if(command.Action==TradeAction.Accept && current.Phase==TradePhase.Invited)
            { if(id!=current.Second) outcome=CraftOutcome.Busy; else { current.Phase=TradePhase.Negotiating; current.Revision++; PublishTrade(current); } }
            else if(current.Phase!=TradePhase.Negotiating) outcome=CraftOutcome.Busy;
            else if(command.Action==TradeAction.Offer)
            {
                var offer=new ExchangeOffer(command.Items.ToArray(),command.Materials.ToArray()); outcome=Inventory!.ValidateOffer(id,offer);
                if(outcome==CraftOutcome.Accepted) { if(id==current.First) current.A=offer; else current.B=offer; current.Revision++; current.AcceptedA=current.AcceptedB=false; current.Expires=Combat!.Time+60; PublishTrade(current); }
            }
            else if(command.Action==TradeAction.Accept)
            {
                if(current.A.Items.Count+current.B.Items.Count+current.A.Materials.Count+current.B.Materials.Count==0) outcome=CraftOutcome.Unavailable;
                else
                {
                    if(id==current.First) current.AcceptedA=true; else current.AcceptedB=true;
                    if(current.AcceptedA && current.AcceptedB)
                    {
                        if(_nodeAudit.Count>=128) outcome=CraftOutcome.RateLimited;
                        else outcome=Inventory!.Exchange(current.First,current.Second,current.Id,current.A,current.B);
                        if(outcome==CraftOutcome.Accepted)
                        { MarkPersistent(current.First); MarkPersistent(current.Second); Audit(_persistentActors.GetValueOrDefault(connection,"runtime-"+connection),"Trade",$"Transfer {Guid.NewGuid():N}, session {current.Id}"); CloseTrade(current,TradePhase.Completed); }
                        else { current.AcceptedA=current.AcceptedB=false; PublishTrade(current); }
                    }
                    else PublishTrade(current);
                }
            }
            else outcome=CraftOutcome.InvalidOperation;
            _tradeResults[id]=new(command.Sequence,Tick,outcome);
        }
        _tradePending.Clear();
    }
    public void ClearTradeResults() { _tradeStates.Clear(); _tradeResults.Clear(); }
    private void RemoveTradePlayer(int connection,NetworkEntityId id)
    { if(_playerTrades.TryGetValue(id,out var session) && _trades.TryGetValue(session,out var trade)) CloseTrade(trade,TradePhase.Cancelled); _tradePending.Remove(connection); _tradeRequests.Remove(connection); _tradeStates.Remove(id); _tradeResults.Remove(id); }
}
