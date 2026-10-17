using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.Networking;

/// <summary>Session ingress guard. Client time bounds input age, never advances simulation or rewinds hits.</summary>
internal sealed class MovementInputWindow(NetworkEntityId owner, int tickRate)
{
    internal NetworkEntityId Owner { get; } = owner;
    private const double BurstCapacity = 12;
    private const double PacketsPerSecond = NetworkConstants.MovementInputRate * 3;
    internal const int MaximumLeadSeconds = 2;
    internal const int MaximumAgeSeconds = 10;
    private double _tokens = BurstCapacity;
    private long? _lastReceived, _originTime;
    private uint _originTick, _lastTick, _lastSequence;

    public bool Accept(MoveCommand command, long receivedMilliseconds)
    {
        if (_lastReceived is { } previous)
            _tokens = Math.Min(BurstCapacity, _tokens + Math.Max(0, receivedMilliseconds - previous) * PacketsPerSecond / 1000);
        _lastReceived = receivedMilliseconds;
        if (_tokens < 1) return false;
        _tokens--;
        if (command.Sequence == 0 || _lastSequence != 0 && !MovementSimulation.IsSequenceNewer(command.Sequence, _lastSequence)) return false;
        if (_originTime is { } origin)
        {
            if (command.ClientTick != _lastTick && !MovementSimulation.IsSequenceNewer(command.ClientTick, _lastTick)) return false;
            var elapsedTicks = (receivedMilliseconds - origin) * tickRate / 1000d;
            var clientTicks = unchecked((int)(command.ClientTick - _originTick));
            if (clientTicks - elapsedTicks > tickRate * MaximumLeadSeconds || elapsedTicks - clientTicks > tickRate * MaximumAgeSeconds) return false;
        }
        else { _originTime = receivedMilliseconds; _originTick = command.ClientTick; }
        _lastTick = command.ClientTick;
        _lastSequence = command.Sequence;
        return true;
    }
}
