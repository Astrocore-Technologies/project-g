using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class TradeProtocolTests
{
    private static void Check(NetDataWriter w,Func<NetDataReader,bool> read)
    {var data=w.CopyData();var r=new NetDataReader(data);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));Assert.True(read(r));for(var n=0;n<data.Length;n++){r=new(data[..n]);Assert.False(NetworkProtocol.TryReadMessageType(r,out _)&&read(r));}r=new([..data,0]);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));Assert.False(read(r));}
    [Fact] public void BoundedOffersRoundTripAndMalformedPacketsAreRejected()
    {
        var items=Enumerable.Range(1,4).Select(i=>new TradeItem((ulong)i,new string('я',24),EquipmentSlot.Weapon,12,0,0,60,100)).ToArray();
        Check(NetworkProtocol.Write(new TradeCommand(1,TradeAction.Offer,2,new(3),4,[1,2,3,4],[new(1,999),new(2,999),new(3,999),new(4,999)])),r=>NetworkProtocol.TryReadTradeCommand(r,out _));
        var state=new TradeState(new(1),2,3,new(4),5,TradePhase.Negotiating,false,true,items,[new(1,999)],items,[new(2,999)]);
        Assert.True(NetworkProtocol.Write(state).Length<900);Check(NetworkProtocol.Write(state),r=>NetworkProtocol.TryReadTradeState(r,out _));
        Check(NetworkProtocol.Write(new TradeResult(1,2,CraftOutcome.Accepted)),r=>NetworkProtocol.TryReadTradeResult(r,out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new TradeCommand(1,TradeAction.Offer,2,new(3),4,[1,1],[])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with {OwnItems=[items[0] with {Attack=double.NaN}]}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with {OwnItems=[items[0] with {Current=101}]}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with {OwnMaterials=[new(1,0)]}));
        Assert.False(NetworkProtocol.TryReadTradeState(new(new byte[801]),out _));
    }
}
