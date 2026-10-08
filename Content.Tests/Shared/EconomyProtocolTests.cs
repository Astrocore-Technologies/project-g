using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class EconomyProtocolTests
{
    private static void Check(NetDataWriter w,Func<NetDataReader,bool> read)
    {var data=w.CopyData();var r=new NetDataReader(data);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));Assert.True(read(r));for(var n=0;n<data.Length;n++){r=new(data[..n]);Assert.False(NetworkProtocol.TryReadMessageType(r,out _)&&read(r));}r=new([..data,0]);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));Assert.False(read(r));}
    [Fact] public void EveryActionAndPrivateStateRoundTripWithStrictBounds()
    {
        foreach(var action in Enum.GetValues<EconomyAction>())
        {var item=action is EconomyAction.Evolve or EconomyAction.Enhance or EconomyAction.List;var listing=action is EconomyAction.Buy or EconomyAction.Cancel;var c=new EconomyCommand(1,action,item?2UL:0,item?3UL:0,listing?4UL:0,action==EconomyAction.SellOre?(ushort)999:(ushort)0,action==EconomyAction.List?1000000:0,0);Check(NetworkProtocol.Write(c),r=>NetworkProtocol.TryReadEconomyCommand(r,out _));}
        var command=new EconomyCommand(1,EconomyAction.Enhance,2,3,0,0,0,4);Check(NetworkProtocol.Write(new EconomyQuote(command,5,20,10,new string('я',24),2,-1000000,15,[new(1,999),new(2,999),new(3,999),new(4,999)])),r=>NetworkProtocol.TryReadEconomyQuote(r,out _));
        Check(NetworkProtocol.Write(new EconomyResult(1,2,CraftOutcome.Accepted,EconomyEffect.Destroyed)),r=>NetworkProtocol.TryReadEconomyResult(r,out _));
        Check(NetworkProtocol.Write(new EconomyState(new(1),2,3,1000000,EconomyEffect.Failed,Enumerable.Range(1,8).Select(i=>new EconomyItem((ulong)i,1,5)).ToArray())),r=>NetworkProtocol.TryReadEconomyState(r,out _));
        var listings=Enumerable.Range(1,8).Select(i=>new MarketListing((ulong)i,1000000,false,new((ulong)i,new string('я',24),EquipmentSlot.Weapon,22,0,0,40,90))).ToArray();var market=NetworkProtocol.Write(new MarketState(new(1),2,1000000,listings));Assert.True(market.Length<1000);Check(market,r=>NetworkProtocol.TryReadMarketState(r,out _));
    }
    [Fact] public void InvalidActionPayloadCountsOwnershipAndNonFiniteRiskAreRejected()
    {
        var c=new EconomyCommand(1,EconomyAction.Enhance,2,3,0,0,0,4);var q=new EconomyQuote(c,5,20,10,"Клинок",2,0,15,[new(3,1)]);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(c with {Quantity=1}));Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(c with {Operation=ulong.MaxValue}));Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(q with {AttackGain=double.NaN}));Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(q with {Chance=101}));Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(q with {ValidSeconds=float.PositiveInfinity}));Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(q with {Costs=[new(3,1),new(3,1)]}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new EconomyState(new(1),2,3,-1,EconomyEffect.None,[])));Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new EconomyState(new(1),2,3,0,EconomyEffect.None,[new(1,1,0),new(1,1,0)])));
        Assert.False(NetworkProtocol.TryReadEconomyCommand(new(new byte[48]),out _));Assert.False(NetworkProtocol.TryReadEconomyQuote(new(new byte[141]),out _));Assert.False(NetworkProtocol.TryReadEconomyState(new(new byte[163]),out _));Assert.False(NetworkProtocol.TryReadMarketState(new(new byte[901]),out _));
    }
}
