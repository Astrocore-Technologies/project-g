using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class AbilityLoadoutSegmentsTests
{
    private static AbilityLoadout Large()=>new(new(1),9,10,100,Enumerable.Range(1,9).Select(id=>new AbilityProfile(
        (ushort)id,AbilityForm.Melee,3,.3f,0,0,5,1,0,Area:new(AbilityAreaShape.Sector,3,HalfAngleRadians:1),
        ExecutionForm:CombatActionForm.Toggle,ChannelManaPerSecond:2,ToggleOccupiesAction:false,
        MeleeOriginHeight:1,MeleeThickness:.3f,HealthCost:10,AllowFatalHealthCost:true)).ToArray());

    [Fact]public void TheLargestOwnedProfilesFitBoundedPacketsAndBecomeVisibleOnlyAfterAssembly()
    {
        var expected=Large();var packets=NetworkProtocol.WriteLoadoutPackets(expected);Assert.True(packets.Count>1);
        var assembly=new AbilityLoadoutAssembly();AbilityLoadout? complete=null;
        for(var i=0;i<packets.Count;i++)
        {
            var bytes=packets[i].CopyData();Assert.InRange(bytes.Length+10,2,NetworkConstants.MaxGamePacketBytes);
            Assert.True(NetworkProtocol.TryReadAbilityLoadout(new NetDataReader(bytes,2,bytes.Length),out var part));
            Assert.True(assembly.Accept(part,out complete));
            if(i+1<packets.Count)Assert.Null(complete);
        }
        Assert.NotNull(complete);Assert.Equal(expected.Abilities,complete.Value.Abilities);
        Assert.All(complete.Value.Abilities,p=>{Assert.Equal(10,p.HealthCost);Assert.True(p.AllowFatalHealthCost);});
    }
    [Fact]public void ReorderedOrMixedOwnerSegmentsAreRejected()
    {
        var parts=NetworkProtocol.WriteLoadoutPackets(Large()).Select(packet=>
        {var bytes=packet.CopyData();Assert.True(NetworkProtocol.TryReadAbilityLoadout(new NetDataReader(bytes,2,bytes.Length),out var part));return part;}).ToArray();
        var assembly=new AbilityLoadoutAssembly();Assert.False(assembly.Accept(parts[1],out _));
        Assert.True(assembly.Accept(parts[0],out _));
        Assert.False(assembly.Accept(parts[1] with {EntityId=new(2)},out _));
    }
}
