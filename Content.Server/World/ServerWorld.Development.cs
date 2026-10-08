using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private readonly HashSet<int> _developmentRevives = new();
    private readonly Dictionary<int, uint> _developmentReviveSequences = new();
    // Permission is supplied by server transport, not a wire flag. Missing permission fails closed.
    public bool TryQueueDevelopmentRevive(int connection, DevelopmentReviveCommand command, bool authorized = false)
    {
        if (!authorized || !_playersByConnection.TryGetValue(connection, out var player) || Combat is null ||
            Combat.Get(player.EntityId).Health > 0 || command.Sequence == 0 ||
            !MovementSimulation.IsSequenceNewer(command.Sequence, _developmentReviveSequences.GetValueOrDefault(connection))) return false;
        _developmentReviveSequences[connection] = command.Sequence;
        return _developmentRevives.Add(connection);
    }
    private void ApplyDevelopmentRevives()
    {
        foreach (var connection in _developmentRevives)
        {
            var player = _playersByConnection[connection];
            if (Combat!.Get(player.EntityId).Health > 0) continue;
            Combat.DevelopmentRevive(player.EntityId);
            if(HasPvp&&_pvp.TryGetValue(player.EntityId,out var state)){_pvp[player.EntityId]=state with {Dead=false};DirtyPvp(player.EntityId);_lethal.Remove(player.EntityId);} _persistenceDirty.Add(connection);
            Echoes?.Wake(player.EntityId);
        }
        _developmentRevives.Clear();
    }
}
