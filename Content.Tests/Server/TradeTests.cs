using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Server.World;
using Xunit;
namespace Content.Tests.Server;
public sealed class TradeTests
{
    private static (ServerWorld World,NetworkEntityId A,NetworkEntityId B) Setup(bool full=false)
    {
        var w=CraftingTests.World();
        var a=CraftingTests.Materials(w,new SavedMaterial(1,3));
        a=a with { Inventory=a.Inventory! with { Items=a.Inventory.Items.Select(i=>i with { EquippedSlot=EquipmentSlot.None }).ToArray() } };
        var b=CraftingTests.Materials(w,new SavedMaterial(2,4));
        b=b with { Inventory=b.Inventory! with { Items=full?Enumerable.Range(0,8).Select(_=>new SavedItem(Guid.NewGuid(),a.Inventory.Items[0].DefinitionId,EquipmentSlot.None)).ToArray():b.Inventory.Items.Select(i=>i with { EquippedSlot=EquipmentSlot.None }).ToArray() } };
        return(w,w.AddPlayer(42,new(1),a).EntityId,w.AddPlayer(43,new(2),b).EntityId);
    }
    private static CraftOutcome Do(ServerWorld w,int conn,uint seq,TradeAction action,TradeState? s,NetworkEntityId partner,IReadOnlyList<ulong>? items=null,IReadOnlyList<MaterialAmount>? mats=null)
    {
        Assert.True(w.TryQueueTrade(conn,new(seq,action,s?.SessionId??0,partner,s?.Revision??0,items??[],mats??[])));
        w.Simulate(.05f);var owner=w.Players.Single(p=>p.ConnectionId==conn).EntityId;return w.TradeResults[owner].Outcome;
    }
    private static TradeState Start(ServerWorld w,NetworkEntityId a,NetworkEntityId b)
    {Assert.Equal(CraftOutcome.Accepted,Do(w,42,1,TradeAction.Invite,null,b));Assert.Equal(CraftOutcome.Accepted,Do(w,43,1,TradeAction.Accept,w.TradeStates[b],a));return w.TradeStates[a];}
    [Fact] public void SwapPreservesUuidsAndMaterialsAndCannotReplayCompletedSession()
    {
        var (w,a,b)=Setup();var ia=w.CaptureCharacter(42).Inventory!;var ib=w.CaptureCharacter(43).Inventory!;
        var ha=w.Inventory!.State(a,0).Items[0].Handle;var hb=w.Inventory.State(b,0).Items[0].Handle;
        var s=Start(w,a,b);
        Assert.Equal(CraftOutcome.Accepted,Do(w,42,2,TradeAction.Offer,s,b,[ha],[new(1,2)]));
        Assert.Equal(CraftOutcome.Accepted,Do(w,43,2,TradeAction.Offer,w.TradeStates[b],a,[hb],[new(2,3)]));
        Assert.Equal(CraftOutcome.Accepted,Do(w,42,3,TradeAction.Accept,w.TradeStates[a],b));
        Assert.Equal(CraftOutcome.Accepted,Do(w,43,3,TradeAction.Accept,w.TradeStates[b],a));
        Assert.Equal(TradePhase.Completed,w.TradeStates[a].Phase);
        Assert.Contains(w.CaptureCharacter(42).Inventory!.Items,i=>i.InstanceId==ib.Items[0].InstanceId);
        Assert.DoesNotContain(w.CaptureCharacter(42).Inventory!.Items,i=>i.InstanceId==ia.Items[0].InstanceId);
        Assert.DoesNotContain(w.Inventory.State(a,w.Tick).Items,i=>i.Handle==ha||i.Handle==hb);
        Assert.Equal((ushort)1,w.CraftState(a).Materials.Single(m=>m.Id==1).Quantity);
        Assert.Equal((ushort)3,w.CraftState(a).Materials.Single(m=>m.Id==2).Quantity);
        Assert.Equal(CraftOutcome.Unavailable,Do(w,43,4,TradeAction.Accept,w.TradeStates[b],a));
        Assert.Equal(ia.Items.Length,w.CaptureCharacter(42).Inventory!.Items.Length);
    }
    [Fact] public void ChangedOfferResetsConfirmationsAndStaleRevisionCannotComplete()
    {
        var (w,a,b)=Setup();var s=Start(w,a,b);var h=w.Inventory!.State(a,0).Items[0].Handle;
        Do(w,42,2,TradeAction.Offer,s,b,[h]);Do(w,42,3,TradeAction.Accept,w.TradeStates[a],b);
        var old=w.TradeStates[b];Assert.True(old.PartnerAccepted);
        Do(w,43,2,TradeAction.Offer,old,a,[],[new(2,1)]);
        Assert.False(w.TradeStates[a].OwnAccepted);Assert.False(w.TradeStates[b].OwnAccepted);
        Assert.Equal(CraftOutcome.InvalidOperation,Do(w,43,3,TradeAction.Accept,old,a));
        Assert.Equal(TradePhase.Negotiating,w.TradeStates[a].Phase);
    }
    [Fact] public void FullInventoryRejectsAtomicallyAndCancellationReleasesReservation()
    {
        var (w,a,b)=Setup(true);var before=w.CaptureCharacter(42).Inventory!.Serialize();var s=Start(w,a,b);
        Do(w,42,2,TradeAction.Offer,s,b,[w.Inventory!.State(a,0).Items[0].Handle],[new(1,2)]);
        Do(w,42,3,TradeAction.Accept,w.TradeStates[a],b);
        Assert.Equal(CraftOutcome.InventoryFull,Do(w,43,2,TradeAction.Accept,w.TradeStates[b],a));
        Assert.Equal(before,w.CaptureCharacter(42).Inventory!.Serialize());
        Assert.Equal(CraftOutcome.Busy,CraftingTests.Do(w,42,1,CraftAction.Make,1));
        Assert.Equal(CraftOutcome.Accepted,Do(w,42,4,TradeAction.Cancel,w.TradeStates[a],b));
        Assert.Equal(CraftOutcome.MissingMaterials,CraftingTests.Do(w,42,1,CraftAction.Make,2));
        Assert.Equal(8,w.CaptureCharacter(43).Inventory!.Items.Length);
    }
    [Fact] public void ForeignOfferAndDisconnectCannotTransferItems()
    {
        var (w,a,b)=Setup();var before=w.CaptureCharacter(42).Inventory!.Serialize();var s=Start(w,a,b);
        Assert.NotEqual(CraftOutcome.Accepted,Do(w,42,2,TradeAction.Offer,s,b,[w.Inventory!.State(b,0).Items[0].Handle]));
        w.RemovePlayer(43);Assert.Equal(TradePhase.Cancelled,w.TradeStates[a].Phase);
        Assert.Equal(before,w.CaptureCharacter(42).Inventory!.Serialize());
        Assert.Equal(CraftOutcome.MissingMaterials,CraftingTests.Do(w,42,1,CraftAction.Make,2));
    }
}
