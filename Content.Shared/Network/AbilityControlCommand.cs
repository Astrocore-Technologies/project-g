using System.Numerics;

namespace Content.Shared.Network;

public enum AbilityControlKind : byte { Aim, Release }

/// <summary>An update to one existing charge, never a new skill execution.</summary>
public readonly record struct AbilityControlCommand(uint Sequence,uint ActionSequence,uint ObservedServerTick,
    AbilityControlKind Kind,Vector2 Aim,float AimHeight=0,float DirectionY=0,ulong ExecutionId=0);

/// <summary>Transport coalescing: a release cannot be overwritten by a late aim heartbeat.</summary>
public sealed class AbilityControlIntentBuffer
{
    public AbilityControlCommand? Current {get;private set;}
    public void Add(AbilityControlCommand command)
    {
        if(Current is {} old && unchecked((int)(command.Sequence-old.Sequence))<=0)return;
        if(Current is {Kind:AbilityControlKind.Release} release && release.ActionSequence==command.ActionSequence &&
            release.ExecutionId==command.ExecutionId && command.Kind==AbilityControlKind.Aim)return;
        Current=command;
    }
    public void Clear()=>Current=null;
}
