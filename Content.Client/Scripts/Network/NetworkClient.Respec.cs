using Content.Shared.Network;
using LiteNetLib;
namespace ProjectG.Networking;

public partial class NetworkClient
{
    private uint _respecSequence;
    public event Action<RespecQuote>? RespecQuoteReceived;
    public event Action<RespecResult>? RespecResultReceived;
    public RespecCommand NewRespecCommand(NetworkEntityId npc,RespecQuote? quote=null)=>new(++_respecSequence,
        quote is null?RespecAction.Quote:RespecAction.Confirm,npc,quote?.QuoteId??0,quote?.Operation??0);
    public void SendRespec(RespecCommand command)
    {if(_handshakeComplete)SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);}
}
