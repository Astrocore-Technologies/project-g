using System.Numerics;

namespace Content.Shared.Network;

public enum EchoCommandOutcome : byte { Accepted, NotOwned, InvalidState, Cooldown, OutOfRange, Blocked, RateLimited, Refused, NoMana, NoStamina, Busy, NeedsWeapon, Capacity, NoHealth }
public enum EchoActionKind : byte { BasicAttack, Signature }
public readonly record struct EchoSpawn(NetworkEntityId EntityId, NetworkEntityId OwnerId, byte Slot, uint ServerTick, Vector2 Position, string Name, float Height = 0);
public readonly record struct EchoSignatureCommand(uint Sequence, uint ClientTick, byte Slot, Vector2 Aim, float AimHeight = 0,
    float DirectionY=0,float DashDistance=0,AbilityIntent Intent=AbilityIntent.Use,NetworkEntityId EntityId=default);
public enum EchoSignatureControlKind : byte { Aim, Release, Cancel }
public readonly record struct EchoSignatureControlCommand(uint Sequence,NetworkEntityId EntityId,uint ActionSequence,
    ulong ExecutionId,uint ObservedServerTick,EchoSignatureControlKind Kind,Vector2 Aim,float AimHeight=0,float DirectionY=0);

/// <summary>At most three active actors. Terminal controls survive a later aim heartbeat.</summary>
public sealed class EchoSignatureControlBuffer
{
    private readonly List<EchoSignatureControlCommand> _commands=new(NetworkConstants.MaxActiveEchoes);
    public IReadOnlyList<EchoSignatureControlCommand> Commands=>_commands;
    public void Add(EchoSignatureControlCommand command)
    {
        var index=_commands.FindIndex(c=>c.EntityId==command.EntityId);
        if(index>=0)
        {
            var old=_commands[index];
            if(unchecked((int)(command.Sequence-old.Sequence))<=0)return;
            if(old.ActionSequence==command.ActionSequence && old.ExecutionId==command.ExecutionId && (byte)old.Kind>(byte)command.Kind)return;
            _commands.RemoveAt(index);
        }
        else if(_commands.Count>=NetworkConstants.MaxActiveEchoes)return;
        _commands.Add(command);
    }
    public void Clear()=>_commands.Clear();
}
public readonly record struct EchoSignatureResult(uint Sequence, uint ServerTick, byte Slot, EchoCommandOutcome Outcome, float CooldownSeconds);
public readonly record struct EchoAction(NetworkEntityId EntityId, uint ServerTick, EchoActionKind Kind,
    NetworkEntityId TargetId, Vector2 Position, float Radius, double Damage, double TargetHealth, float Height = 0);
public readonly record struct EchoSlot(byte Slot, NetworkEntityId EntityId, float Range, float Radius, float CooldownSeconds,bool SignatureRefused=false);
public readonly record struct EchoLoadout(NetworkEntityId OwnerId, uint ServerTick, IReadOnlyList<EchoSlot> Slots);
