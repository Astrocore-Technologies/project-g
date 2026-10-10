using System.Numerics;
using Content.Server.Regions;
using Content.Shared.Navigation;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Navigation;

public sealed class SurfaceProtocolTests
{
    private static NetDataReader Reader(NetDataWriter writer)
    { var reader=new NetDataReader(writer.CopyData()); Assert.True(NetworkProtocol.TryReadMessageType(reader,out _));return reader; }
    [Fact]
    public void HeightSnapshotsAndMoveIntentRoundTripAndRejectTruncationNaNAndTrailingBytes()
    {
        var move=new MoveCommand(1,7,new(8,8),8,123);Assert.True(NetworkProtocol.TryReadMoveCommand(Reader(NetworkProtocol.Write(move)),out var read));Assert.Equal(move,read);
        var entity=new EntitySnapshot(new(1),new(8,8),5,new(12,8),Height:4,TargetHeight:8,DashHeight:-3);
        var packet=NetworkProtocol.Write(new WorldSnapshot(8,[entity]));Assert.True(NetworkProtocol.TryReadWorldSnapshot(Reader(packet),out var snapshot));Assert.Equal(entity,Assert.Single(snapshot.Entities));
        foreach(var size in Enumerable.Range(0,packet.Length)) { var data=packet.CopyData()[..size];var r=new NetDataReader(data);if(NetworkProtocol.TryReadMessageType(r,out _))Assert.False(NetworkProtocol.TryReadWorldSnapshot(r,out _)); }
        var bytes=packet.CopyData().Concat(new byte[]{0}).ToArray();var extra=new NetDataReader(bytes);NetworkProtocol.TryReadMessageType(extra,out _);Assert.False(NetworkProtocol.TryReadWorldSnapshot(extra,out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(move with {TargetHeight=float.NaN}));
        var count=NetworkProtocol.SnapshotCapacity(1200-NetworkProtocol.RegionEnvelopeBytes);var states=Enumerable.Range(1,count).Select(i=>entity with {EntityId=new((ulong)i)}).ToArray();
        var inner=NetworkProtocol.Write(new WorldSnapshot(8,states));var wrapped=new NetDataWriter();NetworkProtocol.WrapRegion(wrapped,1,inner);Assert.InRange(wrapped.Length,2,1200);
    }
    [Fact]
    public void OrderedSurfaceChunksAreBoundedAndContentRevisionIsVerified()
    {
        var geometry=SurfaceCatalog.Load(Path.Combine(AppContext.BaseDirectory,SurfaceCatalog.PackagePath),null)["terrain_test"];var bytes=geometry.Encode();var assembly=new SurfaceAssembly();SurfaceGeometry? assembled=null;
        for(var offset=0;offset<bytes.Length;offset+=NetworkProtocol.SurfaceChunkBytes)
        {
            var chunk=new SurfaceChunk(geometry.Hash,bytes.Length,offset,bytes.AsSpan(offset,Math.Min(NetworkProtocol.SurfaceChunkBytes,bytes.Length-offset)).ToArray());var wire=NetworkProtocol.Write(chunk);Assert.InRange(wire.Length,2,1200);
            Assert.True(NetworkProtocol.TryReadSurfaceChunk(Reader(wire),out var decoded));assembled=assembly.Add(decoded);
        }
        Assert.NotNull(assembled);Assert.Equal(geometry.Hash,assembled.Hash);
        var first=new SurfaceChunk(geometry.Hash,bytes.Length,0,bytes[..NetworkProtocol.SurfaceChunkBytes]);var duplicate=new SurfaceAssembly();duplicate.Add(first);Assert.Throws<InvalidDataException>(()=>duplicate.Add(first));
        Assert.Throws<InvalidDataException>(()=>new SurfaceAssembly().Add(first with {TotalBytes=int.MaxValue}));
        geometry.Vertices[0]=geometry.Vertices[0] with {Y=geometry.Vertices[0].Y+1};Assert.Throws<InvalidDataException>(()=>SurfaceGeometry.Decode(geometry.Encode()));
    }
    [Fact]
    public void ReadinessAndElevatedGatesRoundTrip()
    {
        var entry=new RegionEnter(2,"dungeon_test",new(6,-6),1,123,[new(new(-18,14),1,-3)],true,4);
        Assert.True(NetworkProtocol.TryReadRegionEnter(Reader(NetworkProtocol.Write(entry)),out var read));Assert.Equal(entry with {AdditionalGates=null},read with {AdditionalGates=null});Assert.Equal(entry.AdditionalGates,read.AdditionalGates);
        foreach(var type in new[]{NetworkMessageType.RegionReady,NetworkMessageType.RegionApplied,NetworkMessageType.RegionActivated,NetworkMessageType.RegionBaseline})
        {var state=new RegionLoadState(type,123);Assert.True(NetworkProtocol.TryReadRegionLoadState(Reader(NetworkProtocol.Write(state)),type,out var result));Assert.Equal(state,result);}
    }
}
