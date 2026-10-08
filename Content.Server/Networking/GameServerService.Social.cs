using Content.Shared.Network;
using LiteNetLib;
namespace Content.Server.Networking;
public sealed partial class GameServerService
{
    private readonly HashSet<int> socialSent=new();
    private readonly Dictionary<int,uint> socialPresenceSent=new();
    private void SendSocial(NetPeer peer)
    {
        var world = WorldFor(peer.Id);
        var social=world.Social;
        if(social is null || social.Dirty.Count>0 || !world.TryGetOwnedEntity(peer.Id,out _))return;
        var character=world.SocialCharacter(peer.Id);
        if(!socialSent.Contains(peer.Id)||social.Changed.Contains(character))
        {
            foreach(var roster in world.SocialRosters(peer.Id))SendGame(peer, NetworkProtocol.Write(roster),DeliveryMethod.ReliableOrdered);
            SendGame(peer, NetworkProtocol.Write(world.SocialInvites(peer.Id)),DeliveryMethod.ReliableOrdered);
            socialSent.Add(peer.Id);
        }
        if(world.SocialResults.TryGetValue(peer.Id,out var result))SendGame(peer, NetworkProtocol.Write(result),DeliveryMethod.ReliableOrdered);
        if(!socialPresenceSent.TryGetValue(peer.Id,out var last)||world.Tick-last>=(uint)Math.Max(1,_options.TickRate/2))
        { foreach(var presence in world.PartyPresence(peer.Id))SendGame(peer, NetworkProtocol.Write(presence),DeliveryMethod.Unreliable);socialPresenceSent[peer.Id]=world.Tick; }
    }
}
