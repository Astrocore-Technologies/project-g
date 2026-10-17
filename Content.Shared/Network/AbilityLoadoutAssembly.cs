namespace Content.Shared.Network;

/// <summary>Bounded owner-profile assembly for reliable ordered segments; never exposes a partial loadout.</summary>
public sealed class AbilityLoadoutAssembly
{
    private AbilityLoadout? _header;
    private readonly List<AbilityProfile> _profiles=new(NetworkConstants.MaxAbilityProfiles);
    private byte _next;
    public bool Accept(AbilityLoadout part,out AbilityLoadout? completed)
    {
        completed=null;
        if(part.Segments==1){if(_header is not null)return false;completed=part;return true;}
        if(part.Segment==0)
        {
            if(_header is not null)return false;
            _header=part;_next=0;_profiles.Clear();
        }
        if(_header is not {} header||part.EntityId!=header.EntityId||part.ServerTick!=header.ServerTick||
            part.Mana!=header.Mana||part.MaxMana!=header.MaxMana||part.LastInputSequence!=header.LastInputSequence||
            part.Segments!=header.Segments||part.TotalProfiles!=header.TotalProfiles||part.Segment!=_next||
            _profiles.Count+part.Abilities.Count>header.TotalProfiles)return false;
        foreach(var profile in part.Abilities)
        {
            foreach(var previous in _profiles)if(previous.Id==profile.Id)return false;
            _profiles.Add(profile);
        }
        if(++_next!=header.Segments)return true;
        if(_profiles.Count!=header.TotalProfiles)return false;
        completed=header with {Abilities=_profiles.ToArray(),Segment=0,Segments=1,TotalProfiles=0};
        _header=null;_profiles.Clear();return true;
    }
}
