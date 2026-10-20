using Content.Server.Persistence;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class EchoSimulation
{
    internal SavedEchoCombat PrepareExperience(NetworkEntityId owner,Guid instance,int amount,int recipients)
    {
        var saved=_owners[owner].Entries[instance].Saved;
        var share=SavedExperienceFraction.Add(saved.ExperienceFraction,amount,recipients);
        var next=saved with {Experience=checked(saved.Experience+share.Whole),ExperienceFraction=share.Remainder};
        next.Validate();return next;
    }
    internal void ApplyExperience(NetworkEntityId owner,SavedEchoCombat next)
    {
        _owners[owner].Entries[next.InstanceId].Saved=next;_dirty.Add(owner);_rosterDirty.Add(owner);
    }
}
