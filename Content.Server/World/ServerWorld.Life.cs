using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private readonly Dictionary<int,RespawnCommand> _respawnPending=new();
    private readonly Dictionary<int,(RespawnCommand Command,uint Tick,RespawnOutcome Outcome)> _respawnRequests=new();
    private readonly Dictionary<NetworkEntityId,RespawnResult> _respawnResults=new();
    private readonly Dictionary<int,RespawnCommand> _respawnTransfers=new();
    internal IReadOnlyDictionary<NetworkEntityId,RespawnResult> RespawnResults=>_respawnResults;

    public bool TryQueueRespawn(int connection,RespawnCommand command)
    {
        if(!_playersByConnection.TryGetValue(connection,out var player) || !player.Loaded || command.Sequence==0 || command.DeathId==0 || command.DeathId>long.MaxValue)return false;
        if(_respawnRequests.TryGetValue(connection,out var previous))
        {
            if(command==previous.Command)
            {
                if(_respawnTransfers.ContainsKey(connection))return false;
                _respawnResults[player.EntityId]=new(command.Sequence,Tick,previous.Outcome==RespawnOutcome.Accepted?RespawnOutcome.AlreadyProcessed:previous.Outcome);return false;
            }
            if(!MovementSimulation.IsSequenceNewer(command.Sequence,previous.Command.Sequence) || previous.Tick==Tick)return false;
        }
        _respawnRequests[connection]=(command,Tick,RespawnOutcome.InvalidState);_respawnPending[connection]=command;return true;
    }
    private void SimulateRespawns()
    {
        foreach(var (connection,command) in _respawnPending)
        {
            var player=_playersByConnection[connection];var life=Combat!.CaptureLife(player.EntityId);
            var outcome=command.DeathId!=life.DeathId?RespawnOutcome.StaleDeath:!life.Dead?RespawnOutcome.InvalidState:
                life.RespawnSeconds>0?RespawnOutcome.NotReady:RespawnOutcome.Accepted;
            if(outcome==RespawnOutcome.Accepted && RespawnHome(player.EntityId) is {} home && home.RegionId!=RegionId)
            {
                _respawnTransfers[connection]=command;_respawnRequests[connection]=(command,Tick,outcome);
                continue; // Accepted state/result is published only with the committed destination bootstrap.
            }
            if(outcome==RespawnOutcome.Accepted)RespawnPlayer(player);
            _respawnRequests[connection]=(command,Tick,outcome);_respawnResults[player.EntityId]=new(command.Sequence,Tick,outcome);
        }
        _respawnPending.Clear();
    }
    private void RespawnPlayer(ServerPlayer player)
    {
        var id=player.EntityId;var home=RespawnHome(id);
        if(home is not null && home.RegionId!=RegionId)throw new InvalidOperationException("Foreign tavern requires an atomic regional respawn.");
        var spawn=home is not null?new System.Numerics.Vector2(home.Spawn.X,home.Spawn.Z):HasPvp?Rules.Respawn:_spawn;
        var height=home?.Spawn.Height??(HasPvp?LocateHeight(spawn):_spawnHeight);
        if(!Navigation.IsOnSurface(new(spawn.X,height,spawn.Y)))throw new InvalidOperationException("Configured respawn is not navigable.");
        GroundItems?.CancelChannel(id,Tick);player.Motion.Reset(spawn,spawn,height,height);_movingPlayers.Remove(player.ConnectionId);
        _spatial.Move(id,spawn);Combat!.Move(id,spawn,height);ResetRespawnResources(player);
    }

    private void ResetRespawnResources(ServerPlayer player)
    {
        var id=player.EntityId;
        Combat!.Respawn(id);Abilities!.Respawn(id);Echoes?.Respawn(id);
        if(HasPvp)
        {
            _pvp[id]=_pvp[id] with {Dead=false,CombatUntil=0,ProtectionUntil=PvpClock()+Rules.ProtectionSeconds*1000L};DirtyPvp(id);
            Audit(_persistentActors.GetValueOrDefault(player.ConnectionId,"runtime"),"Respawn",_pvp[id].DeathId.ToString());
        }
        MarkPersistent(id);
    }

    internal bool TryGetRegionalRespawn(int connection,out RespawnCommand command,out Content.Server.Respawn.TavernDefinition? home)
    {
        home=null;if(!_respawnTransfers.TryGetValue(connection,out command))return false;
        var player=_playersByConnection[connection];var life=Combat!.CaptureLife(player.EntityId);
        if(!life.Dead||life.DeathId!=command.DeathId||life.RespawnSeconds>0)
        {
            _respawnTransfers.Remove(connection);_respawnResults[player.EntityId]=new(command.Sequence,Tick,RespawnOutcome.InvalidState);
            _respawnRequests[connection]=(command,Tick,RespawnOutcome.InvalidState);return false;
        }
        home=RespawnHome(player.EntityId)??throw new InvalidOperationException("Regional respawn lost its binding.");
        return home.RegionId!=RegionId;
    }
    internal void PrepareRegionalRespawn(int connection)=>ResetRespawnResources(_playersByConnection[connection]);
    internal void CompleteRegionalRespawn(int connection,RespawnCommand command)
    {
        var player=_playersByConnection[connection];
        _respawnRequests[connection]=(command,Tick,RespawnOutcome.Accepted);
        _respawnResults[player.EntityId]=new(command.Sequence,Tick,RespawnOutcome.Accepted);
    }
    private void ActorRevived(NetworkEntityId id)
    {
        Abilities?.ResumeAfterRevival(id);
        if(!_playersByEntity.ContainsKey(id)){ResumeResurrectedNpc(id);return;}
        Echoes?.Wake(id);
        if(_pvp.TryGetValue(id,out var legal) && legal.Dead)
        {_pvp[id]=legal with {Dead=false};DirtyPvp(id);}
        MarkPersistent(id);
    }
    private void ResetLifeSession(int connection)
    {
        ResetJournalSession(connection);
        _respawnPending.Remove(connection);_respawnRequests.Remove(connection);_respawnTransfers.Remove(connection);
        if(_playersByConnection.TryGetValue(connection,out var player))_respawnResults.Remove(player.EntityId);
    }
    internal void ClearLifeResults(){_respawnResults.Clear();_journalReplies.Clear();_tavernResults.Clear();_tavernDirty.Clear();ClearRespecResults();}
}
