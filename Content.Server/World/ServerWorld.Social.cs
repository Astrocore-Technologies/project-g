using Content.Database;
using Content.Server.Social;
using Content.Shared.Network;
using System.Numerics;
namespace Content.Server.World;
public sealed partial class ServerWorld
{
    public SocialSimulation? Social { get; private set; }
    private bool advanceSocial = true;
    private readonly Dictionary<Guid,long> socialOffline=new();
    public void ScheduleSocialOffline(int connection)
    { if(Social is null||!_playersByConnection.TryGetValue(connection,out var p))return; var s=_pvp[p.EntityId]; socialOffline[_pvpIdentities[p.EntityId]]=Math.Max(s.CombatUntil,s.AggressorUntil); }
    private readonly Dictionary<int, SocialCommand> socialPending = new();
    public Dictionary<int, SocialResult> SocialResults { get; } = new();
    public void EnableSocial(IReadOnlyList<DatabaseSocialRow> rows,int maxParties=64,int maxGuilds=32)
    {
        if (!HasPvp || Social is not null) throw new InvalidOperationException("Invalid social initialization.");
        Social = new(() => PvpClock(), c => {
            var id = _pvpIdentities.FirstOrDefault(p => p.Value == c).Key;
            return !id.IsValid || CanChangeSocial(_playersByEntity[id].ConnectionId);
        },maxParties,maxGuilds);
        Social.Restore(rows);
    }
    internal void AttachSocial(SocialSimulation authority)
    {
        if (!HasPvp || Social is not null || Players.Count != 0)
            throw new InvalidOperationException("Shared social authority must be attached before regional login.");
        Social = authority; advanceSocial = false;
    }
    internal bool CanChangeSocial(int connection)
    {
        var id = _playersByConnection[connection].EntityId;
        return _pvp[id].CombatUntil <= PvpClock() && _pvp[id].AggressorUntil <= PvpClock() &&
            !Combat!.Get(id).IsCasting && !Abilities!.HasActiveEffects(id);
    }
    public Guid SocialCharacter(int connection) => _playersByConnection.TryGetValue(connection, out var p) ? _pvpIdentities.GetValueOrDefault(p.EntityId) : Guid.Empty;
    public void BindSocial(int connection, Guid character)
    { if (Social is not null) { if(socialOffline.Remove(character,out var until))Social.SetOffline(character,until); Social.SetOnline(character); } }
    public void DisconnectSocial(int connection)
    {
        if (Social is null || !_playersByConnection.TryGetValue(connection, out var p)) return;
        ScheduleSocialOffline(connection); socialPending.Remove(connection); SocialResults.Remove(connection);
    }
    public bool TryQueueSocial(int connection, SocialCommand command)
    {
        if (Social is null || !_playersByConnection.ContainsKey(connection) || !NetworkProtocol.ValidSocialCommand(command) || socialPending.ContainsKey(connection)) return false;
        socialPending.Add(connection,command); return true;
    }
    private void SimulateSocial()
    {
        if (Social is null) return;
        foreach(var (character,until) in socialOffline) Social.SetOffline(character,until); socialOffline.Clear();
        if (advanceSocial) Social.Advance();
        foreach (var (connection, original) in socialPending)
        {
            if (!_playersByConnection.TryGetValue(connection,out var p)) continue;
            var character=_pvpIdentities[p.EntityId];
            if(Social.TryReplay(character,original,out var replay)){SocialResults[connection]=new(original.Operation,Tick,replay);continue;}
            var command=original;
            if (command.Action == SocialAction.Invite)
            {
                var target = new NetworkEntityId(command.Target);
                if (!_playersByEntity.TryGetValue(target,out var other) || other.ConnectionId < 0 || Vector2.DistanceSquared(p.Position,other.Position) > _interest.Radius*_interest.Radius)
                { command=command with{Target=0}; }
                else command = command with { Target = Social.Identity(_pvpIdentities[target]).Handle };
            }
            GroundItems?.CancelChannel(p.EntityId,Tick);
            SocialResults[connection]=new(command.Operation,Tick,Social.Execute(character,command,original));
            MarkPersistent(p.EntityId);
        }
        socialPending.Clear();
    }
    public IEnumerable<SocialRoster> SocialRosters(int connection)
    {
        var character=SocialCharacter(connection); var identity=Social!.Identity(character); var owner=_playersByConnection[connection].EntityId;
        foreach(var kind in new[]{SocialKind.Party,SocialKind.Guild})
        {
            var g=Social.Group(character,kind);
            if(g is null) { yield return new(owner,Tick,identity.Handle,identity.LastOperation,kind,0,0,0,0,"",0,0,[]); continue; }
            for(var page=0;page*8<g.Members.Length;page++) yield return new(owner,Tick,identity.Handle,identity.LastOperation,kind,g.Id,g.Revision,g.Capacity,Social.Identity(g.Leader).Handle,g.Name,(byte)page,(byte)g.Members.Length,g.Members.Skip(page*8).Take(8).Select(m=>new SocialMember(Social.Identity(m.Character).Handle,m.Role,Social.IsOnline(m.Character))).ToArray());
        }
    }
    public SocialInvites SocialInvites(int connection)
    {
        var character=SocialCharacter(connection);
        return new(_playersByConnection[connection].EntityId,Tick,Social!.Invitations(character).Select(i=>{
            var g=Social.Groups.Single(g=>g.Id==i.Group);
            return new SocialInvite(i.Token,g.Kind,Social.Identity(i.From).Handle,g.Id,g.Revision,g.Name,i.Handoff,(float)Math.Clamp((i.Until-PvpClock())/1000d,.001,30));
        }).ToArray());
    }
    public IEnumerable<PartyPresence> PartyPresence(int connection)
    {
        var g=Social!.Group(SocialCharacter(connection),SocialKind.Party); if(g is null) yield break;
        var owner=_playersByConnection[connection].EntityId;
        for(var page=0;page*8<g.Members.Length;page++)
        {
            var members=g.Members.Skip(page*8).Take(8).Select(m=>{
                var id=_pvpIdentities.FirstOrDefault(i=>i.Value==m.Character).Key;
                if(!Social.IsOnline(m.Character)||!id.IsValid) return new PartyMemberPresence(Social.Identity(m.Character).Handle,(byte)(id.IsValid?4:0),0,0,Vector2.Zero);
                var a=Combat!.Get(id);return new PartyMemberPresence(Social.Identity(m.Character).Handle,(byte)(1|(a.Health<=0?2:0)),(float)a.Health,(float)a.Stats.MaxHealth,a.Position);
            }).ToArray();
            yield return new(owner,Tick,g.Id,g.Revision,(byte)page,members);
        }
    }
}
