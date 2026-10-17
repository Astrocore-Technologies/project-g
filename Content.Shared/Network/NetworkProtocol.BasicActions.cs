using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(BasicControlCommand command)
    {
        if(!ValidBasicControl(command))throw new ArgumentException("Invalid basic control intent.");
        var writer=CreateWriter(NetworkMessageType.BasicControlCommand);
        writer.Put(command.Sequence);writer.Put(command.ActionSequence);writer.Put(command.ExecutionId);
        writer.Put((byte)command.Kind);WriteVector2(writer,command.Direction);writer.Put(command.DirectionY);return writer;
    }

    public static bool TryReadBasicControlCommand(NetDataReader reader,out BasicControlCommand command)
    {
        command=default;
        if(reader.AvailableBytes!=29 || !reader.TryGetUInt(out var sequence) || !reader.TryGetUInt(out var action) ||
            !reader.TryGetULong(out var execution) || !reader.TryGetByte(out var kind) || !TryReadVector2(reader,out var direction) ||
            !reader.TryGetFloat(out var directionY))return false;
        var value=new BasicControlCommand(sequence,action,execution,(BasicControlKind)kind,direction,directionY);
        if(!ValidBasicControl(value))return false;command=value;return true;
    }

    internal static bool ValidBasicControl(BasicControlCommand command)=>command.Sequence!=0 && command.ActionSequence!=0 &&
        Enum.IsDefined(command.Kind) && BasicAttackShape.IsValidDirection3D(command.Direction,command.DirectionY);

    public static NetDataWriter Write(BasicInputState state)
    {
        if(!state.ActorId.IsValid)throw new ArgumentException("Invalid basic owner.");
        var writer=CreateWriter(NetworkMessageType.BasicInputState);writer.Put(state.ActorId.Value);writer.Put(state.ServerTick);
        writer.Put(state.LastInputSequence);writer.Put(state.LastControlSequence);return writer;
    }

    public static bool TryReadBasicInputState(NetDataReader reader,out BasicInputState state)
    {
        state=default;
        if(reader.AvailableBytes!=20 || !reader.TryGetULong(out var actor) || actor==0 || !reader.TryGetUInt(out var tick) ||
            !reader.TryGetUInt(out var input) || !reader.TryGetUInt(out var control))return false;
        state=new(new(actor),tick,input,control);return true;
    }

    public static NetDataWriter Write(BasicActionState state)
    {
        if(!ValidBasicAction(state))throw new ArgumentException("Invalid basic execution state.");
        var writer=CreateWriter(NetworkMessageType.BasicActionState);
        writer.Put(state.ActorId.Value);writer.Put(state.ExecutionId);writer.Put(state.Sequence);writer.Put(state.ServerTick);
        writer.Put((byte)state.Phase);WriteVector2(writer,state.Position);WriteVector2(writer,state.Direction);
        WriteHeight(writer,state.Height);writer.Put(state.Range);writer.Put(state.HalfAngle);
        writer.Put(state.PreparationSeconds);writer.Put(state.ActiveSeconds);writer.Put(state.RecoverySeconds);writer.Put(state.RemainingSeconds);
        writer.Put(state.MovementMultiplier);writer.Put(state.CanTurn);writer.Put((byte)state.AllowedCancels);
        writer.Put(state.DirectionY);writer.Put(state.OriginHeightOffset);writer.Put(state.Thickness);writer.Put((byte)state.Delivery);return writer;
    }

    public static bool TryReadBasicActionState(NetDataReader reader,out BasicActionState state)
    {
        state=default;
        if(reader.AvailableBytes!=104 || !reader.TryGetULong(out var actor) || !reader.TryGetULong(out var execution) ||
            !reader.TryGetUInt(out var sequence) || !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var phase) ||
            !TryReadVector2(reader,out var position) || !TryReadVector2(reader,out var direction) || !ReadHeight(reader,out var height) ||
            !reader.TryGetFloat(out var range) || !reader.TryGetFloat(out var angle) ||
            !reader.TryGetDouble(out var preparation) || !reader.TryGetDouble(out var active) || !reader.TryGetDouble(out var recovery) ||
            !reader.TryGetDouble(out var remaining) || !reader.TryGetFloat(out var movement) ||
            !reader.TryGetByte(out var turn) || turn>1 || !reader.TryGetByte(out var cancel) ||
            !reader.TryGetFloat(out var directionY) || !reader.TryGetFloat(out var originHeight) || !reader.TryGetFloat(out var thickness) || !reader.TryGetByte(out var delivery))return false;
        var value=new BasicActionState(new(actor),execution,sequence,tick,(CombatActionPhase)phase,position,direction,height,range,angle,
            preparation,active,recovery,remaining,movement,turn==1,(ActionCancelPermission)cancel,directionY,originHeight,thickness,(BasicAttackDelivery)delivery);
        if(!ValidBasicAction(value))return false;state=value;return true;
    }

    private static bool ValidBasicAction(BasicActionState state)
    {
        const ActionCancelPermission known=ActionCancelPermission.Voluntary|ActionCancelPermission.Skill|ActionCancelPermission.Block|
            ActionCancelPermission.Parry|ActionCancelPermission.Dodge|ActionCancelPermission.BasicAttack|ActionCancelPermission.Movement;
        return state.ActorId.IsValid && state.ExecutionId!=0 && state.Sequence!=0 && Enum.IsDefined(state.Phase) &&
            Finite(state.Position) && BasicAttackShape.IsValidDirection3D(state.Direction,state.DirectionY) && float.IsFinite(state.Height) && MathF.Abs(state.Height)<=128 &&
            ValidBasicVolume(state.OriginHeightOffset,state.Thickness,state.Range) &&
            float.IsFinite(state.Range) && state.Range>0 && float.IsFinite(state.HalfAngle) && state.HalfAngle>=0 && state.HalfAngle<=MathF.PI &&
            Enum.IsDefined(state.Delivery) && (state.Delivery==BasicAttackDelivery.Projectile ? state.HalfAngle==0 && state.Thickness==0 : state.HalfAngle>0) &&
            double.IsFinite(state.PreparationSeconds) && state.PreparationSeconds>=0 && double.IsFinite(state.ActiveSeconds) && state.ActiveSeconds>=0 &&
            double.IsFinite(state.RecoverySeconds) && state.RecoverySeconds>=0 && double.IsFinite(state.RemainingSeconds) && state.RemainingSeconds>=0 &&
            double.IsFinite(state.PreparationSeconds+state.ActiveSeconds+state.RecoverySeconds) &&
            state.PreparationSeconds+state.ActiveSeconds+state.RecoverySeconds>0 &&
            float.IsFinite(state.MovementMultiplier) && state.MovementMultiplier>=0 && (state.AllowedCancels&~known)==0 &&
            (state.Phase!=CombatActionPhase.Finished || state.RemainingSeconds==0);
    }
}
