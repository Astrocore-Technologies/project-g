using Content.Shared.Network;
using LiteNetLib;
namespace Content.Server.Networking;
public sealed partial class GameServerService
{
    private readonly HashSet<int> socialSent=new();
    private readonly Dictionary<int,uint> socialPresenceSent=new();
    private void SendSocial(NetPeer peer)
    {
        var social=_world.Social;
        if(social is null || social.Dirty.Count>0 || !_world.TryGetOwnedEntity(peer.Id,out _))return;
        var character=_world.SocialCharacter(peer.Id);
        if(!socialSent.Contains(peer.Id)||social.Changed.Contains(character))
        {
            foreach(var roster in _world.SocialRosters(peer.Id))peer.Send(NetworkProtocol.Write(roster),DeliveryMethod.ReliableOrdered);
            peer.Send(NetworkProtocol.Write(_world.SocialInvites(peer.Id)),DeliveryMethod.ReliableOrdered);
            socialSent.Add(peer.Id);
        }
        if(_world.SocialResults.TryGetValue(peer.Id,out var result))peer.Send(NetworkProtocol.Write(result),DeliveryMethod.ReliableOrdered);
        if(!socialPresenceSent.TryGetValue(peer.Id,out var last)||_world.Tick-last>=(uint)Math.Max(1,_options.TickRate/2))
        { foreach(var presence in _world.PartyPresence(peer.Id))peer.Send(NetworkProtocol.Write(presence),DeliveryMethod.Unreliable);socialPresenceSent[peer.Id]=_world.Tick; }
    }
}
