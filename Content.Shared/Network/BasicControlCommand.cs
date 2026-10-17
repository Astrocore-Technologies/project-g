using System.Numerics;
using Content.Shared.Movement;

namespace Content.Shared.Network;

public enum BasicControlKind : byte { Aim, Cancel }

/// <summary>Controls one owned basic input/execution; never starts an attack or claims a contact.</summary>
public readonly record struct BasicControlCommand(uint Sequence,uint ActionSequence,ulong ExecutionId,
    BasicControlKind Kind,Vector2 Direction,float DirectionY=0);

/// <summary>A later aim must not erase cancellation of the same input at a storage barrier.</summary>
public sealed class BasicControlIntentBuffer
{
    public BasicControlCommand? Current {get;private set;}
    public void Add(BasicControlCommand command)
    {
        if(Current is {} old && !MovementSimulation.IsSequenceNewer(command.Sequence,old.Sequence))return;
        if(Current is {Kind:BasicControlKind.Cancel} cancel && cancel.ActionSequence==command.ActionSequence &&
            cancel.ExecutionId==command.ExecutionId && command.Kind==BasicControlKind.Aim)return;
        Current=command;
    }
    public void Clear()=>Current=null;
}

/// <summary>Private runtime sequence floors, sent to the owner when entering/reattaching.</summary>
public readonly record struct BasicInputState(NetworkEntityId ActorId,uint ServerTick,uint LastInputSequence,uint LastControlSequence);
