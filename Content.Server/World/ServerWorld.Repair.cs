using System.Numerics;
using Content.Shared.Network;
namespace Content.Server.World;
public sealed partial class ServerWorld
{
    private readonly Dictionary<int,RepairCommand> _repairPending=new();
    private readonly Dictionary<int,uint> _repairRequestTicks=new();
    private readonly Dictionary<int,(RepairQuote Quote,double Expires)> _repairQuotes=new();
    private readonly Dictionary<NetworkEntityId,RepairQuote> _repairQuoteResults=new();
    private readonly Dictionary<NetworkEntityId,RepairResult> _repairResults=new();
    public IReadOnlyDictionary<NetworkEntityId,RepairQuote> RepairQuotes => _repairQuoteResults;
    public IReadOnlyDictionary<NetworkEntityId,RepairResult> RepairResults => _repairResults;
    public bool TryQueueRepair(int connection,RepairCommand command)
    {
        if(!HasCrafting || !_playersByConnection.TryGetValue(connection,out var player) || command.Operation is 0 or >long.MaxValue || command.ItemHandle==0 || command.ItemRevision is 0 or >long.MaxValue || command.QuoteId>long.MaxValue) return false;
        if(_repairRequestTicks.TryGetValue(connection,out var tick) && tick==Tick) { _repairPending.Remove(connection); _repairResults[player.EntityId]=new(command.Operation,Tick+1,CraftOutcome.RateLimited); return false; }
        GroundItems?.CancelChannel(player.EntityId,Tick); _repairRequestTicks[connection]=Tick; _repairPending[connection]=command; return true;
    }
    private void SimulateRepair()
    {
        foreach(var (connection,command) in _repairPending)
        {
            var player=_playersByConnection[connection]; var id=player.EntityId;
            var outcome=Inventory!.CheckRepairOperation(id,command);
            if(outcome==CraftOutcome.Accepted)
            {
                var actor=Combat!.Get(id);
                if(actor.Health<=0) outcome=CraftOutcome.InvalidState;
                else if(actor.IsCasting || player.Motion.IsMoving || player.Motion.IsDashing || Abilities!.HasActiveEffects(id)) outcome=CraftOutcome.Busy;
                else if(Vector2.DistanceSquared(player.Position,_crafting!.StationPosition)>_crafting.InteractionRange*_crafting.InteractionRange) outcome=CraftOutcome.TooFar;
                else if(!Navigation.CanTraverse(player.Position,_crafting!.StationPosition)) outcome=CraftOutcome.Blocked;
                else if(!Inventory.TryRepairInfo(id,command.ItemHandle,out var revision,out var material,out var quantity) || revision!=command.ItemRevision) outcome=CraftOutcome.Unavailable;
                else if(command.QuoteId==0)
                {
                    if(_repairQuotes.TryGetValue(connection,out var old) && old.Expires>Combat.Time && old.Quote.Operation==command.Operation && old.Quote.ItemHandle==command.ItemHandle && old.Quote.ItemRevision==revision)
                        _repairQuoteResults[id]=old.Quote with { ServerTick=Tick,ValidSeconds=(float)(old.Expires-Combat.Time) };
                    else { var quote=new RepairQuote(command.Operation,command.ItemHandle,revision,(ulong)Random.Shared.NextInt64(1,long.MaxValue),Tick,material,quantity,15); _repairQuotes[connection]=(quote,Combat.Time+15); _repairQuoteResults[id]=quote; }
                    continue;
                }
                else if(!_repairQuotes.TryGetValue(connection,out var saved) || saved.Expires<=Combat.Time || saved.Quote.Operation!=command.Operation || saved.Quote.QuoteId!=command.QuoteId || saved.Quote.ItemHandle!=command.ItemHandle || saved.Quote.ItemRevision!=revision || saved.Quote.MaterialId!=material || saved.Quote.Quantity!=quantity) outcome=CraftOutcome.InvalidOperation;
                else outcome=Inventory.Repair(id,command,material,quantity);
            }
            if(outcome==CraftOutcome.Accepted) { MarkPersistent(id); _repairQuotes.Remove(connection); }
            _repairResults[id]=new(command.Operation,Tick,outcome);
        }
        _repairPending.Clear();
    }
    public void ClearRepairResults() { _repairResults.Clear(); _repairQuoteResults.Clear(); }
    private void RemoveRepairPlayer(int connection,NetworkEntityId id) { _repairPending.Remove(connection); _repairRequestTicks.Remove(connection); _repairQuotes.Remove(connection); _repairResults.Remove(id); _repairQuoteResults.Remove(id); }
}
