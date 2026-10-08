using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Network;
public sealed class StarterZoneProtocolTests
{
    private static NetDataReader Body(NetDataWriter w) { var r=new NetDataReader(w.CopyData()); Assert.True(NetworkProtocol.TryReadMessageType(r,out _)); return r; }
    private static ExplorationState Map() => new(new(1),7,30,30,127,new byte[113],[new(1,"Старая роща",new(-9,-6)),new(2,"Путевой камень",new(9,6))]);
    [Fact] public void PublicZoneAndOwnerMapRoundTripWithStrictBounds()
    {
        var zone=new StarterZoneState("Берега Переправы","Пристань",new(-10,0),"Лада",new(-11,-1));
        Assert.True(NetworkProtocol.TryReadStarterZoneState(Body(NetworkProtocol.Write(zone)),out var read)); Assert.Equal(zone,read);
        var map=Map(); map.Cells[112]=15;
        Assert.True(NetworkProtocol.TryReadExplorationState(Body(NetworkProtocol.Write(map)),out var restored)); Assert.Equal(map.Cells,restored.Cells); Assert.Equal(map.Places,restored.Places); Assert.Equal(map.Tutorial,restored.Tutorial);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(map with { Width=65535,Height=65535 }));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(map with { Tutorial=128 }));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(map with { OwnerId=default }));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(map with { Places=[map.Places[0],map.Places[0]] }));
        map.Cells[112]=128; Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(map));
    }
    [Fact] public void EveryTruncationTrailingBytesOversizedNamesAndTailBitsAreRejected()
    {
        var data=NetworkProtocol.Write(Map()).CopyData();
        for(var i=1;i<data.Length;i++) Assert.False(NetworkProtocol.TryReadExplorationState(new NetDataReader(data[1..i]),out _));
        Assert.False(NetworkProtocol.TryReadExplorationState(new NetDataReader(data[1..].Concat(new byte[1]).ToArray()),out _));
        var bad=(byte[])data.Clone(); bad[20+112]=128; Assert.False(NetworkProtocol.TryReadExplorationState(new NetDataReader(bad[1..]),out _));
        Assert.False(NetworkProtocol.TryReadExplorationState(new NetDataReader(new byte[365]),out _));
        var name=new string('a',33); Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new StarterZoneState(name,"Town",Vector2.Zero,"Guide",Vector2.Zero)));
        var zone=NetworkProtocol.Write(new StarterZoneState("Region","Town",Vector2.Zero,"Guide",Vector2.Zero)).CopyData();
        for(var i=1;i<zone.Length;i++) Assert.False(NetworkProtocol.TryReadStarterZoneState(new NetDataReader(zone[1..i]),out _));
        Assert.False(NetworkProtocol.TryReadStarterZoneState(new NetDataReader(zone[1..].Concat(new byte[1]).ToArray()),out _));
    }
}
