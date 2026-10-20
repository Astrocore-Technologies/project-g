using Content.Shared.Network;
using LiteNetLib;

namespace ProjectG.Networking;

public partial class NetworkClient
{
    public TavernState? LatestTavern {get;private set;}
    public event Action<TavernState>? TavernReceived;
    public event Action<TavernResult>? TavernResultReceived;
    private uint _respawnSequence,_tavernSequence;
    public RespawnCommand NewRespawnCommand(ulong death)=>new(++_respawnSequence,death);
    public TavernCommand NewTavernCommand(NetworkEntityId npc)=>new(++_tavernSequence,npc);
    public void SendTavern(TavernCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
}
