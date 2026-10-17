using Content.Server.Items;
using Content.Server.Persistence;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    internal ItemWearLedger ItemWear {get;private set;}=new();
    internal void UseItemWearLedger(ItemWearLedger ledger)
    {
        if(ReferenceEquals(ledger,ItemWear))return;
        if(!ItemWear.IsEmpty)throw new InvalidOperationException("Regional item ledger composition is startup-only.");
        ItemWear.Detach(ApplyEscrowWear);ItemWear=ledger;ItemWear.Attach(ApplyEscrowWear);Inventory?.UseWearLedger(ledger);
    }
    private bool ApplyEscrowWear(Guid uuid,long total)
    {
        if(_nodeState.DeathLoot is {} loot)
            for(var i=0;i<loot.Length;i++)if(loot[i].Item.InstanceId==uuid)
            {
                var updated=loot[i] with {Item=ItemWearDocuments.Settle(ItemWearDocuments.Apply(loot[i].Item,total))};
                var next=loot.ToArray();next[i]=updated;_nodeState=_nodeState with {DeathLoot=next};
                GroundItems?.UpdateDeathLoot(updated);Audit("combat","WeaponWear",uuid.ToString("N"));return true;
            }
        if(_nodeState.Market is {} market)
            for(var i=0;i<market.Listings.Length;i++)if(market.Listings[i].Item.InstanceId==uuid)
            {
                var listings=market.Listings.ToArray();listings[i]=listings[i] with
                    {Item=ItemWearDocuments.Settle(ItemWearDocuments.Apply(listings[i].Item,total))};
                _nodeState=_nodeState with {Market=market with {Listings=listings}};
                MarketDirty=true;
                Audit("combat","WeaponWear",uuid.ToString("N"));return true;
            }
        return false;
    }
}
