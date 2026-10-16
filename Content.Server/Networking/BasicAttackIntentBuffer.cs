using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.Networking;

/// <summary>Two bounded slots while persistence owns the simulation barrier. A release is terminal until a new press.</summary>
internal sealed class BasicAttackIntentBuffer
{
    internal AttackCommand? Start { get; private set; }
    internal AttackCommand? Control { get; private set; }
    private uint _lastSequence;

    internal void Add(AttackCommand command)
    {
        if (command.Sequence==0 || !MovementSimulation.IsSequenceNewer(command.Sequence,_lastSequence)) return;
        _lastSequence=command.Sequence;
        if(command.Mode is BasicAttackMode.Manual or BasicAttackMode.BeginContinuous)
        { Start=command; Control=null; return; }
        // Coalescing an update must not erase an earlier release and resurrect a hold.
        Control=Control is {Mode:BasicAttackMode.EndContinuous}
            ? command with {Mode=BasicAttackMode.EndContinuous} : command;
    }
}
