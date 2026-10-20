using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;

public sealed class ActivityProtocolTests
{
    private static NetDataReader Body(byte[] packet)=>new(packet,2,packet.Length);
    [Fact]public void ActivityIntentAndResultHaveExactBoundedLayouts()
    {
        foreach(var action in Enum.GetValues<ActivityAction>())
        {
            var value=new ActivityCommand(3,action,new(7),123);var bytes=NetworkProtocol.Write(value).CopyData();
            Assert.True(NetworkProtocol.TryReadActivityCommand(Body(bytes),out var read));Assert.Equal(value,read);
            for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadActivityCommand(Body(bytes[..i]),out _));
            Assert.False(NetworkProtocol.TryReadActivityCommand(Body([..bytes,0]),out _));bytes[6]=255;Assert.False(NetworkProtocol.TryReadActivityCommand(Body(bytes),out _));
        }
        var result=new ActivityResult(1,2,3,QuestOutcome.AlreadyProcessed);var packet=NetworkProtocol.Write(result).CopyData();
        Assert.True(NetworkProtocol.TryReadActivityResult(Body(packet),out var decoded));Assert.Equal(result,decoded);
        for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadActivityResult(Body(packet[..i]),out _));
        Assert.False(NetworkProtocol.TryReadActivityResult(Body([..packet,0]),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ActivityCommand(1,ActivityAction.Deliver,new(1),0)));
    }
    [Fact]public void OwnerStateCarriesOnlyCurrentPublicActivityAndPrivateOrderWithinPacketBudget()
    {
        var s=new ActivityState(new(1),2,"Древесина для лагеря","Доставьте древесину смотрителю.","Древесина",3,6,20,1,QuestStatus.Active,new(2),new(3),1,CampWatchPhase.Active,20);
        var bytes=NetworkProtocol.Write(s).CopyData();Assert.InRange(bytes.Length+NetworkProtocol.RegionEnvelopeBytes,1,NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadActivityState(Body(bytes),out var actual));Assert.Equal(s,actual);
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadActivityState(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadActivityState(Body([..bytes,0]),out _));
        foreach(var invalid in new[]{s with {Order=0},s with {Carried=1000},s with {WatchOccurrence=0},s with {Giver=default},s with {WatchPhase=(CampWatchPhase)255}})
            Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(invalid));
    }
    [Fact]public void OreQuoteDisclosesBoundedExperienceAndRejectsMalformedPayloads()
    {
        var quote=new EconomyQuote(new(1,EconomyAction.SellOre,0,0,0,3,0,1),2,100,0,"Руда",0,9,15,[],3);
        var bytes=NetworkProtocol.Write(quote).CopyData();
        Assert.True(NetworkProtocol.TryReadEconomyQuote(Body(bytes),out var actual));
        Assert.Equal(quote with {Costs=actual.Costs},actual);
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadEconomyQuote(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadEconomyQuote(Body([..bytes,0]),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(quote with {Experience=-1}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(quote with {Experience=int.MaxValue}));
    }
    [Fact]public void ResourceAndStationHeightsRoundTripRejectNonFiniteTruncationAndTrailingBytes()
    {
        var resource=new ResourceNodeState(1,3,new(-414,224),1,16,"Сухое дерево",14.14f);var bytes=NetworkProtocol.Write(resource).CopyData();
        Assert.True(NetworkProtocol.TryReadResourceNodeState(Body(bytes),out var decoded));Assert.Equal(resource,decoded);
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadResourceNodeState(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadResourceNodeState(Body([..bytes,0]),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(resource with {Height=float.NaN}));
        var recipe=new CraftRecipeState(1,"Слиток","Железный слиток",new(-416,230),"Верстак",2,[new(2,2,"Руда")],14.12f);
        bytes=NetworkProtocol.Write(recipe).CopyData();Assert.True(NetworkProtocol.TryReadCraftRecipeState(Body(bytes),out var read));
        Assert.Equal(recipe with {Costs=read.Costs},read);
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadCraftRecipeState(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadCraftRecipeState(Body([..bytes,0]),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(recipe with {StationHeight=float.PositiveInfinity}));
    }
}
