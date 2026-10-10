using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class CombatInteractionProtocolTests
{
    private static NetDataReader Reader(byte[] bytes)
    { var reader=new NetDataReader(bytes); Assert.True(NetworkProtocol.TryReadMessageType(reader,out _)); return reader; }
    [Fact]
    public void AirborneSnapshotRoundTripsWithinUnreliableBudget()
    {
        var actor=new EntitySnapshot(new(1),Vector2.Zero,1,Vector2.Zero,AirOffset:1,
            Control:CombatControlPhase.Airborne,ControlRemaining:.7f);
        var bytes=NetworkProtocol.Write(new WorldSnapshot(1,Enumerable.Range(1,NetworkConstants.MaxEntitiesPerSnapshot)
            .Select(i=>actor with {EntityId=new((ulong)i)}).ToArray())).CopyData();
        Assert.True(bytes.Length<=1200);
        Assert.True(NetworkProtocol.TryReadWorldSnapshot(Reader(bytes),out var state)); Assert.Equal(actor,state.Entities[0]);
        Assert.Equal(18,NetworkProtocol.SnapshotCapacity(1200));
    }
    [Fact]
    public void QuickRecoverAndRecoveryParametersRoundTrip()
    {
        var command=new DefenseCommand(1,DefenseAction.QuickRecover,Vector2.UnitX);
        Assert.True(NetworkProtocol.TryReadDefenseCommand(Reader(NetworkProtocol.Write(command).CopyData()),out var result)); Assert.Equal(command,result);
        var state=new DefenseState(new(1),2,3,DefenseOutcome.Accepted,85,100,.5,0,0,35,10,false,0,Vector2.UnitY,
            .45,12,15,1.5f,6,.2f);
        Assert.True(NetworkProtocol.TryReadDefenseState(Reader(NetworkProtocol.Write(state).CopyData()),out var actual)); Assert.Equal(state,actual);
        var profile=new AbilityProfile(30,AbilityForm.Melee,1.5f,.3f,0,.35,12,0,0,16,RecoverySeconds:.4);
        var loadout=new AbilityLoadout(new(1),1,10,10,[profile]);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Reader(NetworkProtocol.Write(loadout).CopyData()),out var parsed)); Assert.Equal(profile,Assert.Single(parsed.Abilities));
    }
    [Fact]
    public void NewLayoutsRejectTruncatedTrailingAndInvalidControlData()
    {
        var snapshot=NetworkProtocol.Write(new WorldSnapshot(1,[new(new(1),Vector2.Zero,0,Vector2.Zero)])).CopyData();
        foreach (var change in new[]{-1,1})
        { var bytes=(byte[])snapshot.Clone(); Array.Resize(ref bytes,bytes.Length+change); Assert.False(NetworkProtocol.TryReadWorldSnapshot(Reader(bytes),out _)); }
        var invalid=(byte[])snapshot.Clone(); invalid[67]=255;
        Assert.False(NetworkProtocol.TryReadWorldSnapshot(Reader(invalid),out _));
        BitConverter.GetBytes(float.NaN).CopyTo(invalid,63);
        Assert.False(NetworkProtocol.TryReadWorldSnapshot(Reader(invalid),out _));
        var defense=NetworkProtocol.Write(new DefenseState(new(1),1,1,DefenseOutcome.Accepted,100,100,0,0,0,20,10,false,1,Vector2.UnitY)).CopyData();
        foreach (var change in new[]{-1,1})
        { var bytes=(byte[])defense.Clone(); Array.Resize(ref bytes,bytes.Length+change); Assert.False(NetworkProtocol.TryReadDefenseState(Reader(bytes),out _)); }
        BitConverter.GetBytes(float.NaN).CopyTo(defense,defense.Length-4);
        Assert.False(NetworkProtocol.TryReadDefenseState(Reader(defense),out _));
    }
}
