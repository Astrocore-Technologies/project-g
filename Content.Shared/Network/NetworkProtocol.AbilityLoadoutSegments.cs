using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static IReadOnlyList<NetDataWriter> WriteLoadoutPackets(AbilityLoadout value)
    {
        if(!ValidLoadout(value)||value.Segments!=1)throw new ArgumentException("Expected a complete owned ability loadout.");
        const int headerBytes=38;
        var bytes=headerBytes;
        foreach(var profile in value.Abilities)bytes+=ProfileWireBytes(profile);
        if(bytes<=NetworkConstants.MaxGamePacketBytes-RegionEnvelopeBytes)return [Write(value)];
        var ranges=new List<(int Start,int Count)>();var start=0;bytes=headerBytes;
        for(var i=0;i<value.Abilities.Count;i++)
        {
            var size=ProfileWireBytes(value.Abilities[i]);
            if(headerBytes+size>NetworkConstants.MaxGamePacketBytes-RegionEnvelopeBytes)throw new ArgumentException("An owned skill profile exceeds one packet.");
            if(bytes+size>NetworkConstants.MaxGamePacketBytes-RegionEnvelopeBytes)
            {ranges.Add((start,i-start));start=i;bytes=headerBytes;}
            bytes+=size;
        }
        ranges.Add((start,value.Abilities.Count-start));
        var packets=new NetDataWriter[ranges.Count];
        for(var i=0;i<ranges.Count;i++)
        {
            var range=ranges[i];var profiles=new AbilityProfile[range.Count];
            for(var j=0;j<profiles.Length;j++)profiles[j]=value.Abilities[range.Start+j];
            packets[i]=Write(value with {Abilities=profiles,Segment=(byte)i,Segments=(byte)ranges.Count,TotalProfiles=(byte)value.Abilities.Count});
        }
        return packets;
    }
    private static int ProfileWireBytes(AbilityProfile profile)=>98+(profile.HealthCost>0?8:0)+(profile.Form switch
        {AbilityForm.Projectile=>4,AbilityForm.Melee=>8,AbilityForm.GroundArea=>5,AbilityForm.Dash=>17,_=>0})+
        (profile.ExecutionForm is CombatActionForm.Channel or CombatActionForm.Hold or CombatActionForm.Toggle?24:0)+
        (profile.ExecutionForm is CombatActionForm.Hold or CombatActionForm.Toggle?1:0)+
        (profile.ExecutionForm==CombatActionForm.Charged?10:0);
}
