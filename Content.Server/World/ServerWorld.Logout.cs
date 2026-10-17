using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private readonly Dictionary<int,uint> _logoutPending=new(),_logoutSequences=new();
    private readonly Dictionary<int,LogoutResult> _logoutResults=new();
    internal IReadOnlyDictionary<int,LogoutResult> LogoutResults=>_logoutResults;

    public bool TryQueueLogout(int connection,LogoutCommand command)
    {
        if(!_playersByConnection.TryGetValue(connection,out var player) || !player.Loaded || command.Sequence==0 ||
            !MovementSimulation.IsSequenceNewer(command.Sequence,_logoutSequences.GetValueOrDefault(connection)) || _logoutPending.ContainsKey(connection))return false;
        _logoutSequences[connection]=command.Sequence;_logoutPending[connection]=command.Sequence;return true;
    }
    private void SimulateLogout()
    {
        foreach(var (connection,sequence) in _logoutPending)
            _logoutResults[connection]=new(sequence,Tick,IsCombatTagged(connection)?LogoutOutcome.InCombat:LogoutOutcome.Accepted);
        _logoutPending.Clear();
    }
    internal void ClearLogoutResults()=>_logoutResults.Clear();
    internal void FinishDeparture(int connection)
    {
        if(!_playersByConnection.TryGetValue(connection,out var player))return;
        Abilities?.FinishDeparture(player.EntityId);Inventory?.FlushWear(player.EntityId);
        Echoes?.PrepareOwnerExit(player.EntityId);
    }
    private void ResetLogoutSession(int connection)
    {_logoutPending.Remove(connection);_logoutSequences.Remove(connection);_logoutResults.Remove(connection);}
}
