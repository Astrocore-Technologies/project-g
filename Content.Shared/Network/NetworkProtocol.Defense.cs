using Content.Shared.Combat;
using LiteNetLib.Utils;
namespace Content.Shared.Network;
public static partial class NetworkProtocol
{
    public static bool ValidDefenseCommand(DefenseCommand c) => c.Sequence!=0 && Enum.IsDefined(c.Action) && BasicAttackShape.IsValidDirection(c.Direction);
    public static NetDataWriter Write(DefenseCommand c)
    {
        if (!ValidDefenseCommand(c)) throw new ArgumentException("Invalid defense command.");
        var w=CreateWriter(NetworkMessageType.DefenseCommand); w.Put(c.Sequence); w.Put((byte)c.Action); WriteVector2(w,c.Direction); return w;
    }
    public static bool TryReadDefenseCommand(NetDataReader r,out DefenseCommand c)
    {
        c=default;
        if (r.AvailableBytes!=13 || !r.TryGetUInt(out var seq) || !r.TryGetByte(out var action) || !TryReadVector2(r,out var direction)) return false;
        var value=new DefenseCommand(seq,(DefenseAction)action,direction); if (!ValidDefenseCommand(value)) return false; c=value; return true;
    }
    private static bool ValidDefense(DefenseState s) => s.OwnerId.IsValid && Enum.IsDefined(s.Outcome) &&
        double.IsFinite(s.Stamina) && s.Stamina>=0 && double.IsFinite(s.MaxStamina) && s.MaxStamina>0 && s.Stamina<=s.MaxStamina &&
        double.IsFinite(s.BlockDamage) && double.IsFinite(s.ParryRemaining) && s.ParryRemaining is >=0 and <=60 &&
        double.IsFinite(s.ParryCooldown) && s.ParryCooldown is >=0 and <=60 && double.IsFinite(s.DodgeCost) && s.DodgeCost>0 &&
        double.IsFinite(s.ParryCost) && s.ParryCost>0 && float.IsFinite(s.MovementMultiplier) && s.MovementMultiplier is >=0 and <=2 && BasicAttackShape.IsValidDirection(s.Direction) &&
        double.IsFinite(s.RecoveryRemaining) && s.RecoveryRemaining is >=0 and <=10 &&
        double.IsFinite(s.QuickRecoverCooldown) && s.QuickRecoverCooldown is >=0 and <=60 &&
        double.IsFinite(s.QuickRecoverCost) && s.QuickRecoverCost > 0 &&
        float.IsFinite(s.QuickRecoverRange) && s.QuickRecoverRange is >0 and <=3 &&
        float.IsFinite(s.QuickRecoverSpeed) && s.QuickRecoverSpeed is >0 and <=20 &&
        float.IsFinite(s.InputBufferSeconds) && s.InputBufferSeconds is >0 and <=.3f;
    public static NetDataWriter Write(DefenseState s)
    {
        if (!ValidDefense(s)) throw new ArgumentException("Invalid defense state.");
        var w=CreateWriter(NetworkMessageType.DefenseState); w.Put(s.OwnerId.Value); w.Put(s.ServerTick); w.Put(s.Sequence); w.Put((byte)s.Outcome);
        w.Put(s.Stamina); w.Put(s.MaxStamina); w.Put(s.BlockDamage); w.Put(s.ParryRemaining); w.Put(s.ParryCooldown); w.Put(s.DodgeCost); w.Put(s.ParryCost);
        w.Put(s.Blocking); w.Put(s.MovementMultiplier); WriteVector2(w,s.Direction); w.Put(s.RecoveryRemaining); w.Put(s.QuickRecoverCooldown); w.Put(s.QuickRecoverCost);
        w.Put(s.QuickRecoverRange); w.Put(s.QuickRecoverSpeed); w.Put(s.InputBufferSeconds); return w;
    }
    public static bool TryReadDefenseState(NetDataReader r,out DefenseState s)
    {
        s=default;
        if (r.AvailableBytes!=122 || !r.TryGetULong(out var id) || !r.TryGetUInt(out var tick) || !r.TryGetUInt(out var seq) || !r.TryGetByte(out var outcome) ||
            !r.TryGetDouble(out var stamina) || !r.TryGetDouble(out var max) || !r.TryGetDouble(out var block) || !r.TryGetDouble(out var window) ||
            !r.TryGetDouble(out var cooldown) || !r.TryGetDouble(out var dodgeCost) || !r.TryGetDouble(out var parryCost) ||
            !r.TryGetByte(out var guarding) || guarding>1 || !r.TryGetFloat(out var speed) || !TryReadVector2(r,out var direction) || !r.TryGetDouble(out var recovery) || !r.TryGetDouble(out var quickCooldown) ||
            !r.TryGetDouble(out var quickCost) || !r.TryGetFloat(out var quickRange) || !r.TryGetFloat(out var quickSpeed) || !r.TryGetFloat(out var buffer)) return false;
        var value=new DefenseState(new(id),tick,seq,(DefenseOutcome)outcome,stamina,max,block,window,cooldown,dodgeCost,parryCost,guarding==1,speed,direction,recovery,quickCooldown,quickCost,quickRange,quickSpeed,buffer);
        if (!ValidDefense(value)) return false; s=value; return true;
    }
}
