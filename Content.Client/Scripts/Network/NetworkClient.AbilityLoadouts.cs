using Content.Shared.Network;

namespace ProjectG.Networking;

public partial class NetworkClient
{
    private readonly Dictionary<NetworkEntityId,AbilityLoadoutAssembly> _abilityLoadoutParts=new();
    private bool ReceiveAbilityLoadout(AbilityLoadout part)
    {
        if(part.Segments==1)
        {
            if(_abilityLoadoutParts.ContainsKey(part.EntityId))return false;
            AbilityLoadoutReceived?.Invoke(part);return true;
        }
        if(!_abilityLoadoutParts.TryGetValue(part.EntityId,out var assembly))
        {
            if(part.Segment!=0||_abilityLoadoutParts.Count>=1+NetworkConstants.MaxActiveEchoes)return false;
            _abilityLoadoutParts.Add(part.EntityId,assembly=new());
        }
        if(!assembly.Accept(part,out var complete))return false;
        if(complete is {} loadout){_abilityLoadoutParts.Remove(part.EntityId);AbilityLoadoutReceived?.Invoke(loadout);}
        return true;
    }
}
