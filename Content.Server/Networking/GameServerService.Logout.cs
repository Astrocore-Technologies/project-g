using Content.Shared.Network;
using LiteNetLib;
namespace Content.Server.Networking;

public sealed partial class GameServerService
{
    private void CollectLogoutResults()
    {
        foreach(var world in _worlds)
            foreach(var (connection,result) in world.LogoutResults)
            {
                if(!_peers.TryGetValue(connection,out var peer))continue;
                if(result.Outcome==LogoutOutcome.Accepted)
                {_logoutApprovals[connection]=result;_departed.Add(connection);}
                else SendGame(peer,NetworkProtocol.Write(result),DeliveryMethod.ReliableOrdered);
            }
    }
}
