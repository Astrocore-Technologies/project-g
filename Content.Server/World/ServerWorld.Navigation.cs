using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private uint ProcessedMovementTick(ServerPlayer player)
    {
        if (player.LastProcessedSequence == 0) return 0;
        var elapsed = unchecked(Tick - player.LastMovementServerTick);
        // The input's ClientTick already includes the first predicted step after submission.
        return unchecked(player.LastMovementClientTick + (elapsed == 0 ? 0 : elapsed - 1));
    }

    internal void ApplyReceivedMove(int connection, MoveCommand command)
    {
        if (TryApplyMove(connection, command) || !_playersByConnection.TryGetValue(connection, out var player)) return;
        if (!MovementSimulation.IsSequenceNewer(command.Sequence, player.LastProcessedSequence) ||
            player.NavigationOrder != 0 && command.InputOrder != player.NavigationOrder &&
            !MovementSimulation.IsSequenceNewer(command.InputOrder, player.NavigationOrder)) return;
        // A reliable rejected destination will not be retried by prediction. Confirm its cancellation,
        // preserving the previously valid server route unless the phase itself already stopped it.
        if (player.CancelledNavigationOrder == 0 || MovementSimulation.IsSequenceNewer(command.InputOrder, player.CancelledNavigationOrder))
            player.CancelledNavigationOrder = command.InputOrder;
    }

    private void CancelNavigationForAction(NetworkEntityId id, uint inputOrder)
    {
        if (!_playersByEntity.TryGetValue(id, out var player) || inputOrder == 0) return;
        // Cancellation happens at actual execution start, including a command that waited in the combat buffer.
        if (player.NavigationOrder != 0 && MovementSimulation.IsSequenceNewer(player.NavigationOrder, inputOrder))
            inputOrder = player.NavigationOrder;
        if (player.CancelledNavigationOrder == 0 || MovementSimulation.IsSequenceNewer(inputOrder, player.CancelledNavigationOrder))
            player.CancelledNavigationOrder = inputOrder;
        if (!player.Motion.IsDashing && Combat?.IsFleeing(id) != true)
        {
            player.Motion.Reset(player.Position, player.Position, player.Height, player.Height);
            _movingPlayers.Remove(player.ConnectionId);
        }
    }

    private bool AcceptNavigationOrder(ServerPlayer player, MoveCommand command)
    {
        if (command.InputOrder == 0 ||
            player.CancelledNavigationOrder != 0 && !MovementSimulation.IsSequenceNewer(command.InputOrder, player.CancelledNavigationOrder) ||
            player.NavigationOrder != 0 && command.InputOrder != player.NavigationOrder &&
                !MovementSimulation.IsSequenceNewer(command.InputOrder, player.NavigationOrder)) return false;
        if (Combat?.OrdinaryMovement(player.EntityId) <= 0 || Combat?.IsFleeing(player.EntityId) == true)
        {
            // Remember a rejected new command, so its later heartbeats cannot start after the phase ends.
            CancelNavigationForAction(player.EntityId, command.InputOrder);
            return false;
        }
        return true;
    }
}
