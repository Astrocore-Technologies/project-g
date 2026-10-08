using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class WorldNodeProtocolTests
{
    private static byte[] Body(NetDataWriter w) => w.CopyData().Skip(2).ToArray();
    [Fact] public void IntentionsAndResultsRoundTripAndRejectMalformedPackets()
    {
        foreach(var action in Enum.GetValues<WorldNodeAction>())
        {
            var value=new WorldNodeCommand(42,action); var bytes=Body(NetworkProtocol.Write(value));
            Assert.True(NetworkProtocol.TryReadWorldNodeCommand(new(bytes),out var copy)); Assert.Equal(value,copy);
            for(var i=0;i<bytes.Length;i++) Assert.False(NetworkProtocol.TryReadWorldNodeCommand(new(bytes.Take(i).ToArray()),out _));
            Assert.False(NetworkProtocol.TryReadWorldNodeCommand(new([..bytes,0]),out _)); bytes[4]=255; Assert.False(NetworkProtocol.TryReadWorldNodeCommand(new(bytes),out _));
        }
        foreach(var outcome in Enum.GetValues<WorldNodeOutcome>())
        {
            var value=new WorldNodeResult(42,7,outcome); var bytes=Body(NetworkProtocol.Write(value)); Assert.True(NetworkProtocol.TryReadWorldNodeResult(new(bytes),out var copy)); Assert.Equal(value,copy);
            for(var i=0;i<bytes.Length;i++) Assert.False(NetworkProtocol.TryReadWorldNodeResult(new(bytes.Take(i).ToArray()),out _));
            Assert.False(NetworkProtocol.TryReadWorldNodeResult(new([..bytes,0]),out _)); bytes[8]=255; Assert.False(NetworkProtocol.TryReadWorldNodeResult(new(bytes),out _));
        }
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new WorldNodeCommand(0,WorldNodeAction.Repair)));
    }
    [Fact] public void PublicStateRoundTripsAtUtf8BudgetWithoutPrivateHistoryOrOperatorSecrets()
    {
        var value=new WorldNodeState(1,3,new(-3,-2),3,new string('Ж',32),new string('界',100),new string('界',100)); var bytes=Body(NetworkProtocol.Write(value));
        Assert.True(NetworkProtocol.TryReadWorldNodeState(new(bytes),out var copy)); Assert.Equal(value,copy);
        for(var i=0;i<bytes.Length;i++) Assert.False(NetworkProtocol.TryReadWorldNodeState(new(bytes.Take(i).ToArray()),out _));
        Assert.False(NetworkProtocol.TryReadWorldNodeState(new([..bytes,0]),out _)); bytes[20]=4; Assert.False(NetworkProtocol.TryReadWorldNodeState(new(bytes),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(value with { KeeperName=new string('x',33) }));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(value with { Position=new(float.NaN,0) }));
        Assert.Equal(7,typeof(WorldNodeState).GetProperties().Length);
    }
    [Fact] public void NavigationUpdatesOnlyOpenCellsAndPreserveSharedMoverReferences()
    {
        var grid=new Content.Shared.Navigation.NavigationGrid(new(new(-2,-2),1,0.4f,4,4,[0,0,0,0,0,1,1,0,0,1,1,0,0,0,0,0]));
        var update=grid.ToMessage(); update.BlockedCells[5]=0; grid.ApplyOpening(update); Assert.False(grid.IsBlocked(1,1));
        update.BlockedCells[0]=1; Assert.Throws<ArgumentException>(()=>grid.ApplyOpening(update)); Assert.False(grid.IsBlocked(0,0));
        Assert.Throws<ArgumentException>(()=>grid.ApplyOpening(update with { Origin=new(0,0) }));
    }
}
