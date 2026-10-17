using Content.Server.Combat;
using Content.Server.Data;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class EchoSimulation
{
    private readonly HashSet<(NetworkEntityId Owner,Guid Instance)> _permissionRefresh=new();

    /// <summary>Server-only notification after relationship state changes; applied before fixed-frame combat input.</summary>
    public bool RefreshPermission(NetworkEntityId owner,Guid instance)
    {
        if(!_owners.TryGetValue(owner,out var state)||!state.Entries.TryGetValue(instance,out var entry)||
            _catalog.Echoes[entry.Saved.DefinitionId].Stars!=5)return false;
        _permissionRefresh.Add((owner,instance));return true;
    }

    private EchoCombatPermission ReadPermission(Entry entry)
    {
        var definition=_catalog.Echoes[entry.Saved.DefinitionId];
        if(definition.Stars!=5 || _permissionSource is null)return EchoCombatPermission.AllowAll;
        var permission=_permissionSource.Read(entry.Saved.InstanceId)??throw new InvalidOperationException("Missing Echo combat permission decision.");
        foreach(var skill in permission.RefusedSkills)
            if(!_catalog.Creatures[definition.ProfileId].AbilityIds.Any(id=>_catalog.Abilities[id].NetworkId==skill))
                throw new InvalidOperationException("Relationship decision references a skill not owned by this Echo.");
        return permission;
    }

    private bool Allows(Entry entry,ushort skill)
    {
        var permission=entry.Permission;
        if(skill==0)return permission.BasicAllowed;
        var signature=_catalog.Abilities[_catalog.Echoes[entry.Saved.DefinitionId].SignatureId].NetworkId;
        return permission.SkillsAllowed && (skill!=signature||permission.SignatureAllowed) && !permission.RefusedSkills.Contains(skill);
    }
    private bool AllowsAction(NetworkEntityId id,ushort skill)=>!_actors.TryGetValue(id,out var body) || Allows(_owners[body.OwnerId].Entries[body.InstanceId],skill);

    private void PreparePermissions()
    {
        foreach(var (owner,instance) in _permissionRefresh)
        {
            if(!_owners.TryGetValue(owner,out var state)||!state.Entries.TryGetValue(instance,out var entry))continue;
            entry.Permission=ReadPermission(entry);_rosterDirty.Add(owner);_loadoutDirty.Add(owner);
            if(entry.Body is not {} body)continue;
            if(!Allows(entry,0))_combat.StopAiBasicIntent(body.Id,body.BasicSequence);
            if(_catalog.Echoes[entry.Saved.DefinitionId].RefusalDuringAction==EchoRefusalActionReaction.InterruptAsEnemy)
            {
                if(!Allows(entry,0))_combat.StopBasic(body.Id,ActionStopReason.EnemyInterrupt);
                _abilities.InterruptCompanionActions(body.Id,skill=>!Allows(entry,skill));
            }
            WakeActor(body.Id);_dirty.Add(owner);
        }
        _permissionRefresh.Clear();
    }
}
