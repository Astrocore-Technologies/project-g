using Content.Server.Combat;
using Content.Shared.Network;

namespace Content.Server.Networking;

internal readonly record struct CombatInputReceipt(NetworkEntityId Actor,string Region,CombatInput Input,double Offset,bool Deferred=false);

/// <summary>One frame of received events, independent of the one-command gameplay waiting buffer.</summary>
internal sealed class CombatInputBuffer
{
    private readonly List<CombatInputReceipt> _events=new(70);
    internal IReadOnlyList<CombatInputReceipt> Events=>_events;
    internal bool Add(CombatInputReceipt receipt)
    {
        if(_events.Count<64){_events.Add(receipt);return true;}
        if(!receipt.Input.Terminal)return false;
        // At capacity preserve the newest terminal control for each family.
        for(var i=64;i<_events.Count;i++)
            if(_events[i].Input.Kind==receipt.Input.Kind){_events.RemoveAt(i);break;}
        _events.Add(receipt);return true;
    }
    internal void Clear()=>_events.Clear();
}
