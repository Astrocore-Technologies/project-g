using Content.Shared.Network;
using LiteNetLib;
namespace ProjectG.Networking;

public partial class NetworkClient
{
    public ActivityState? LatestActivities {get;private set;}
    public event Action<ActivityState>? ActivitiesReceived;
    public event Action<ActivityResult>? ActivityResultReceived;
    public void SendActivity(ActivityCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
}
