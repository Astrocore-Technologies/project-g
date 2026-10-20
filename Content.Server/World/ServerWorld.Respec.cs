using System.Numerics;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Server.Stats;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private RespecServiceDefinition? _respecService;
    private sealed record ResetOffer(RespecQuote Quote,double Expires,SavedProgression Progression,BaseStats Basis,string Inventory,int ServiceRevision);
    private readonly Dictionary<NetworkEntityId,ResetOffer> _resetOffers=new();
    private readonly Dictionary<NetworkEntityId,RespecCommand> _resetPending=new();
    private readonly Dictionary<NetworkEntityId,(RespecCommand Command,uint Tick)> _resetSequences=new();
    private readonly Dictionary<NetworkEntityId,RespecQuote> _resetQuotes=new();
    private readonly Dictionary<NetworkEntityId,RespecResult> _resetResults=new();
    internal IReadOnlyDictionary<NetworkEntityId,RespecQuote> RespecQuotes=>_resetQuotes;
    internal IReadOnlyDictionary<NetworkEntityId,RespecResult> RespecResults=>_resetResults;

    public void ConfigureRespec(RespecServiceDefinition definition)
    {
        if(Players.Count!=0||_respecService is not null||_taverns is null)throw new InvalidOperationException("Configure reset services after taverns and before players.");
        definition.Validate(_taverns);
        if(_innkeepers.Values.Any(t=>definition.Taverns.Contains(t.Id))&&
            (Inventory is null||_progressionCatalog?.Economy is not {} economy||definition.PaidCoins>economy.CoinLimit))
            throw new InvalidDataException("Reset service requires inventory/wallet support.");
        _respecService=definition;
    }
    internal bool TryQueueRespec(int connection,RespecCommand command)
    {
        if(!NetworkProtocol.ValidRespecCommand(command)||!_playersByConnection.TryGetValue(connection,out var player)||!player.Loaded)return false;
        var id=player.EntityId;if(!_progression.ContainsKey(id))return false;
        if(_resetSequences.TryGetValue(id,out var previous))
        {
            var same=command==previous.Command;
            if(!same&&!MovementSimulation.IsSequenceNewer(command.Sequence,previous.Command.Sequence))return false;
            if(previous.Tick==Tick)
            {
                if(!same){_resetPending.Remove(id);_resetSequences[id]=(command,Tick);_resetResults[id]=new(command.Sequence,Tick,command.Operation,RespecOutcome.RateLimited,0,0);}
                return false;
            }
        }
        _resetSequences[id]=(command,Tick);_resetPending[id]=command;return true;
    }
    private RespecOutcome CheckResetService(NetworkEntityId id,RespecCommand command)
    {
        if(_respecService is null||Inventory is null||!_innkeepers.TryGetValue(command.NpcId,out var inn)||!_respecService.Taverns.Contains(inn.Id))return RespecOutcome.Unavailable;
        var actor=Combat!.Get(id);var player=_playersByEntity[id];
        if(actor.Health<=0||!actor.Active)return RespecOutcome.InvalidState;
        if(Combat.InCombat(id))return RespecOutcome.InCombat;
        if(Inventory.IsTrading(id)||actor.IsCasting||player.Motion.IsMoving||player.Motion.IsDashing||Abilities!.HasActiveEffects(id))return RespecOutcome.Busy;
        if(Vector3.DistanceSquared(actor.Foot,inn.Npc.Position)>inn.InteractionRange*inn.InteractionRange||
            !Navigation.ClearAttack(actor.Foot+Vector3.UnitY,inn.Npc.Position+Vector3.UnitY))return RespecOutcome.TooFar;
        return RespecOutcome.Accepted;
    }
    private string ResetInventory(NetworkEntityId id)=> (Inventory!.Capture(id) with {Crafting=null,Maintenance=null}).Serialize();
    private static double[] PrimaryValues(BaseStats s)=>[s.Strength,s.Agility,s.Vitality,s.Intelligence,s.Dexterity,s.Luck,s.Wisdom];
    private bool MatchesResetOffer(NetworkEntityId id,ResetOffer offer)
    {
        var current=_progression[id];var original=offer.Progression;
        return current.Level==original.Level&&current.StatPoints==original.StatPoints&&current.Allocation==original.Allocation&&current.Respec==original.Respec&&
            current.Profession==original.Profession&&current.Skills.AsSpan().SequenceEqual(original.Skills)&&
            _playersByEntity[id].BaseStats==offer.Basis&&ResetInventory(id)==offer.Inventory&&_respecService!.Revision==offer.ServiceRevision&&
            offer.Quote.Price==(current.Respec is null?0:_respecService.PaidCoins)&&
            DisplayStats(Combat!.Get(id).Stats).AsSpan().SequenceEqual(offer.Quote.Current)&&
            DisplayStats(ProjectStats(id,current.Allocation!.StartingStats)).AsSpan().SequenceEqual(offer.Quote.Projected);
    }
    private void SimulateRespec()
    {
        foreach(var (id,command) in _resetPending)
        {
            var value=_progression[id];var receipt=value.Respec;
            if(command.Action==RespecAction.Confirm&&receipt is not null&&command.Operation==(ulong)receipt.Completed&&command.QuoteId==receipt.QuoteId)
            {_resetResults[id]=new(command.Sequence,Tick,command.Operation,RespecOutcome.AlreadyProcessed,receipt.PaidCoins,receipt.ReturnedPoints);continue;}
            var outcome=CheckResetService(id,command);var ledger=value.Allocation;
            if(outcome==RespecOutcome.Accepted)
            {
                if(ledger is null||ledger.StartingStats!=_playerDefinition!.Stats)outcome=RespecOutcome.MigrationRequired;
                else if(ledger.Invested.Total>100000||value.StatPoints+ledger.Invested.Total>100000)outcome=RespecOutcome.Unavailable;
                else if(receipt?.Completed==int.MaxValue)outcome=RespecOutcome.Unavailable;
                else if(command.Action==RespecAction.Confirm&&command.Operation<=(ulong)(receipt?.Completed??0))outcome=RespecOutcome.StaleQuote;
                else if(ledger.Invested.Total==0)outcome=RespecOutcome.NothingToReset;
                else if(command.Action==RespecAction.Quote)
                {
                    var operation=(ulong)(receipt?.Completed??0)+1;var price=receipt is null?0:_respecService!.PaidCoins;
                    if(_resetOffers.TryGetValue(id,out var old)&&old.Quote.NpcId==command.NpcId&&old.Expires>Combat!.Time&&MatchesResetOffer(id,old))
                    {_resetQuotes[id]=old.Quote with {Sequence=command.Sequence,ServerTick=Tick,ValidSeconds=(float)(old.Expires-Combat.Time)};continue;}
                    var returned=checked((int)ledger.Invested.Total);var quote=new RespecQuote(command.Sequence,Tick,command.NpcId,
                        (ulong)Random.Shared.NextInt64(1,long.MaxValue),operation,price,Inventory!.Capture(id).Economy?.Coins??0,
                        returned,checked(value.StatPoints+returned),_respecService!.QuoteSeconds,receipt is null,
                        PrimaryValues(_playersByEntity[id].BaseStats),PrimaryValues(ledger.StartingStats),
                        DisplayStats(Combat!.Get(id).Stats),DisplayStats(ProjectStats(id,ledger.StartingStats)));
                    _resetOffers[id]=new(quote,Combat.Time+quote.ValidSeconds,value,_playersByEntity[id].BaseStats,ResetInventory(id),_respecService.Revision);
                    _resetQuotes[id]=quote;continue;
                }
                else if(!_resetOffers.TryGetValue(id,out var offer)||offer.Expires<=Combat!.Time||offer.Quote.NpcId!=command.NpcId||
                    offer.Quote.QuoteId!=command.QuoteId||offer.Quote.Operation!=command.Operation||!MatchesResetOffer(id,offer))outcome=RespecOutcome.StaleQuote;
                else if(offer.Quote.Coins<offer.Quote.Price)outcome=RespecOutcome.InsufficientCoins;
                else
                {
                    var saved=new SavedRespec(1,checked((int)command.Operation),command.QuoteId,offer.Quote.Price,offer.Quote.ReturnedPoints);
                    var next=value with {Allocation=ledger with {Invested=default},StatPoints=offer.Quote.AvailableAfter,Respec=saved};next.Validate();
                    var prepared=Inventory!.PrepareStatReset(id,ledger.StartingStats,offer.Quote.Price);
                    // All calculations and payment validation precede mutation; the outer checkpoint publishes the whole result.
                    _playersByEntity[id].BaseStats=ledger.StartingStats;_progression[id]=next;Inventory.CommitStatReset(id,prepared);
                    _resetOffers.Remove(id);_allocationQuotes.Remove(id);_statPreviews.Remove(id);DirtyProgression(id);MarkPersistent(id);
                    if(HasWorldNode)Audit(_persistentActors.GetValueOrDefault(_playersByEntity[id].ConnectionId,"runtime"),"StatReset",$"{saved.Completed}, coins {saved.PaidCoins}, points {saved.ReturnedPoints}");
                    _resetResults[id]=new(command.Sequence,Tick,command.Operation,RespecOutcome.Accepted,saved.PaidCoins,saved.ReturnedPoints);continue;
                }
            }
            _resetResults[id]=new(command.Sequence,Tick,command.Operation,outcome,0,0);
        }
        _resetPending.Clear();
    }
    private void ResetRespecSession(NetworkEntityId id)
    {_resetOffers.Remove(id);_resetPending.Remove(id);_resetSequences.Remove(id);_resetQuotes.Remove(id);_resetResults.Remove(id);}
    private void ClearRespecResults(){_resetQuotes.Clear();_resetResults.Clear();}
}
