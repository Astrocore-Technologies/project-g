using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class ProgressionProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    { var r = new NetDataReader(bytes); Assert.True(NetworkProtocol.TryReadMessageType(r,out _)); return r; }
    private static void Check(byte[] bytes, Func<NetDataReader,bool> read)
    { Assert.True(read(Body(bytes))); for (var i=2;i<bytes.Length;i++) Assert.False(read(Body(bytes[..i]))); Assert.False(read(Body([..bytes,0]))); }
    private static ProgressionState State() => new(new(1),10,2,20,200,3,1,[1,2,3,4,5,6],[new(1,2,1,6,8,false),new(5,0,0,0,0,true)]);
    [Fact]
    public void MessagesRoundTripRejectEveryTruncationAndTrailingData()
    {
        foreach (var command in new ProgressionCommand[] { new(1,ProgressionAction.AllocateStat,5,0,0),new(2,ProgressionAction.LearnSkill,0,5,0),new(3,ProgressionAction.AssignSlot,0,1,8) })
        { var bytes = NetworkProtocol.Write(command).CopyData(); Check(bytes,r => NetworkProtocol.TryReadProgressionCommand(r,out _)); Assert.True(NetworkProtocol.TryReadProgressionCommand(Body(bytes),out var parsed)); Assert.Equal(command,parsed); }
        Check(NetworkProtocol.Write(new ProgressionResult(1,2,ProgressionOutcome.Accepted)).CopyData(),r => NetworkProtocol.TryReadProgressionResult(r,out _));
        var state = State(); var data = NetworkProtocol.Write(state).CopyData(); Check(data,r => NetworkProtocol.TryReadProgressionState(r,out _));
        Assert.True(NetworkProtocol.TryReadProgressionState(Body(data),out var actual)); Assert.Equal(state.Skills,actual.Skills); Assert.Equal(state.Stats,actual.Stats); Assert.Equal(state.Level,actual.Level);
        Assert.InRange(data.Length,1,NetworkConstants.MaxGamePacketBytes);
    }
    [Fact]
    public void BatchAndPreviewAreBoundedAndRejectMalformedPayloads()
    {
        foreach (var action in new[] { ProgressionAction.PreviewStats,ProgressionAction.AllocateStats })
        {
            var command = new ProgressionCommand(7,action,0,0,0,[1,0,2,1,1,0]);
            var bytes = NetworkProtocol.Write(command).CopyData();
            Check(bytes,r=>NetworkProtocol.TryReadProgressionCommand(r,out _));
            Assert.True(NetworkProtocol.TryReadProgressionCommand(Body(bytes),out var result));
            Assert.Equal(command.Allocation,result.Allocation);
            foreach (var allocation in new int[][] { [-1,0,0,0,0,0], [int.MaxValue,0,0,0,0,0], [100000,1,0,0,0,0], [1,2] })
                Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(command with { Allocation=allocation }));
            bytes[^1]=255; Assert.False(NetworkProtocol.TryReadProgressionCommand(Body(bytes),out _));
        }
        var preview = new StatPreview(7,42,Enumerable.Repeat(1d,17).ToArray(),Enumerable.Repeat(2d,17).ToArray());
        var data = NetworkProtocol.Write(preview).CopyData(); Check(data,r=>NetworkProtocol.TryReadStatPreview(r,out _));
        Assert.True(NetworkProtocol.TryReadStatPreview(Body(data),out var decoded)); Assert.Equal(preview.Projected,decoded.Projected);
        Assert.InRange(data.Length,1,NetworkConstants.MaxGamePacketBytes);
        BitConverter.GetBytes(double.NaN).CopyTo(data,10); Assert.False(NetworkProtocol.TryReadStatPreview(Body(data),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ProgressionCommand(1,ProgressionAction.AllocateStats,0,0,0,new int[6])));
    }
    [Fact]
    public void RejectsInvalidCommandsEnumsNumbersDuplicateSkillsAndSlots()
    {
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new ProgressionCommand(0,ProgressionAction.AllocateStat,0,0,0)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new ProgressionCommand(1,ProgressionAction.AllocateStat,6,0,0)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new ProgressionCommand(1,ProgressionAction.AssignSlot,0,1,9)));
        var bytes = NetworkProtocol.Write(new ProgressionCommand(1,ProgressionAction.AllocateStat,0,0,0)).CopyData(); bytes[6] = 255;
        Assert.False(NetworkProtocol.TryReadProgressionCommand(Body(bytes),out _));
        var s = State();
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(s with { Stats = [double.NaN,1,1,1,1,1] }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(s with { Skills = [s.Skills[0],s.Skills[0]] }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(s with { Skills = [s.Skills[0],s.Skills[0] with { Id = 2 }] }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(s with { Experience = s.NextExperience }));
        bytes = NetworkProtocol.Write(s).CopyData(); bytes[^1] = 2; Assert.False(NetworkProtocol.TryReadProgressionState(Body(bytes),out _));
    }
}
