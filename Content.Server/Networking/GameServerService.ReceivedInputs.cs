using System.Diagnostics;
using Content.Server.Combat;
using Content.Server.World;
using Content.Shared.Network;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Content.Server.Networking;

public sealed partial class GameServerService
{
    private bool ReceiveCombatPacket(int connection,ServerWorld world,NetworkMessageType type,NetDataReader reader,DeliveryMethod delivery)
    {
        CombatInput input;
        switch(type)
        {
            case NetworkMessageType.AttackCommand:
                if(delivery!=DeliveryMethod.ReliableOrdered||!NetworkProtocol.TryReadAttackCommand(reader,out var attack))break;
                input=new(CombatInputKind.Basic,Attack:attack);ReceiveCombatInput(connection,world,input);return true;
            case NetworkMessageType.DefenseCommand:
                if(!NetworkProtocol.TryReadDefenseCommand(reader,out var defense)||
                    !(delivery==DeliveryMethod.ReliableOrdered||defense.Action==DefenseAction.Block&&delivery==DeliveryMethod.Unreliable))break;
                input=new(CombatInputKind.Defense,Defense:defense);ReceiveCombatInput(connection,world,input);return true;
            case NetworkMessageType.AbilityCommand:
                if(delivery!=DeliveryMethod.ReliableOrdered||!NetworkProtocol.TryReadAbilityCommand(reader,out var ability))break;
                input=new(CombatInputKind.Skill,Skill:ability);ReceiveCombatInput(connection,world,input);return true;
            case NetworkMessageType.BasicControlCommand:
                if(delivery!=DeliveryMethod.ReliableOrdered||!NetworkProtocol.TryReadBasicControlCommand(reader,out var basic))break;
                input=new(CombatInputKind.BasicControl,BasicControl:basic);ReceiveCombatInput(connection,world,input);return true;
            case NetworkMessageType.AbilityControlCommand:
                if(delivery!=DeliveryMethod.ReliableOrdered||!NetworkProtocol.TryReadAbilityControlCommand(reader,out var control))break;
                input=new(CombatInputKind.SkillControl,SkillControl:control);ReceiveCombatInput(connection,world,input);return true;
            case NetworkMessageType.ActionCancelCommand:
                if(delivery!=DeliveryMethod.ReliableOrdered||!NetworkProtocol.TryReadActionCancelCommand(reader,out var cancel))break;
                input=new(CombatInputKind.Cancel,Cancel:cancel);ReceiveCombatInput(connection,world,input);return true;
            default:return false;
        }
        RecordCombatRejection("MalformedOrWrongDelivery",(byte)type);return true;
    }
    private void SendAbilityLoadout(NetPeer peer,AbilityLoadout loadout)
    {foreach(var packet in NetworkProtocol.WriteLoadoutPackets(loadout))SendGame(peer,packet,DeliveryMethod.ReliableOrdered);}
    private readonly Dictionary<int,CombatInputBuffer> _receivedCombat=new();
    private Stopwatch? _inputClock;
    private TimeSpan _inputFrameStart;

    private void ReceiveCombatInput(int connection,ServerWorld world,CombatInput input)
    {
        if(!world.TryGetOwnedEntity(connection,out var owner))return;
        if(!_receivedCombat.TryGetValue(connection,out var buffer))_receivedCombat.Add(connection,buffer=new());
        var offset=_inputClock is null?1d/_options.TickRate:Math.Clamp((_inputClock.Elapsed-_inputFrameStart).TotalSeconds,0,1d/_options.TickRate);
        if(!buffer.Add(new(owner,world.RegionId,input,offset,WaitingForDurability)))RecordCombatRejection("IngressCapacity",(byte)input.Kind);
    }
    private void ApplyReceivedCombatInputs()
    {
        foreach(var (connection,buffer) in _receivedCombat)
        {
            if(_peers.ContainsKey(connection))
            {
                var world=WorldFor(connection);
                foreach(var receipt in buffer.Events)
                    world.QueueReceivedCombatInput(connection,receipt.Actor,receipt.Region,receipt.Input,
                        receipt.Deferred?1d/_options.TickRate:receipt.Offset,1d/_options.TickRate);
            }
            buffer.Clear();
        }
    }
}
