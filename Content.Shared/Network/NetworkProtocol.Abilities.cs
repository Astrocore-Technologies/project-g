using System.Numerics;
using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(AbilityControlCommand value)
    {
        if(value.Sequence==0 || value.ActionSequence==0 || !Enum.IsDefined(value.Kind) || !Finite(value.Aim) ||
            !float.IsFinite(value.AimHeight) || MathF.Abs(value.AimHeight)>128 || !float.IsFinite(value.DirectionY) || MathF.Abs(value.DirectionY)>1)
            throw new ArgumentException("Invalid ability control.");
        var writer=CreateWriter(NetworkMessageType.AbilityControlCommand);
        writer.Put(value.Sequence);writer.Put(value.ActionSequence);writer.Put(value.ObservedServerTick);writer.Put((byte)value.Kind);
        WriteVector2(writer,value.Aim);WriteHeight(writer,value.AimHeight);WriteHeight(writer,value.DirectionY);writer.Put(value.ExecutionId);return writer;
    }
    public static bool TryReadAbilityControlCommand(NetDataReader reader,out AbilityControlCommand value)
    {
        value=default;
        if(reader.AvailableBytes!=37 || !reader.TryGetUInt(out var sequence) || sequence==0 || !reader.TryGetUInt(out var action) || action==0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var kind) || !Enum.IsDefined((AbilityControlKind)kind) ||
            !TryReadVector2(reader,out var aim) || !ReadHeight(reader,out var height) || !ReadHeight(reader,out var dy) || MathF.Abs(dy)>1 ||
            !reader.TryGetULong(out var execution))return false;
        value=new(sequence,action,tick,(AbilityControlKind)kind,aim,height,dy,execution);return true;
    }
    public static NetDataWriter Write(ActionCancelCommand value)
    {
        if(value.Sequence==0 || value.ActionSequence==0) throw new ArgumentException("Invalid action cancellation.");
        var writer=CreateWriter(NetworkMessageType.ActionCancelCommand);
        writer.Put(value.Sequence); writer.Put(value.ActionSequence);writer.Put(value.ExecutionId);writer.Put(value.ObservedServerTick); return writer;
    }

    public static bool TryReadActionCancelCommand(NetDataReader reader,out ActionCancelCommand value)
    {
        value=default;
        if(reader.AvailableBytes!=20 || !reader.TryGetUInt(out var sequence) || sequence==0 ||
            !reader.TryGetUInt(out var actionSequence) || actionSequence==0 || !reader.TryGetULong(out var execution)||!reader.TryGetUInt(out var observed)) return false;
        value=new(sequence,actionSequence,execution,observed); return true;
    }

    public static NetDataWriter Write(AbilityCommand value)
    {
        if (value.Sequence == 0 || value.AbilityId == 0 || !Enum.IsDefined(value.Intent) || !Finite(value.Aim) || !float.IsFinite(value.DashDistance) || value.DashDistance is < 0 or > 100 || !float.IsFinite(value.DirectionY) || MathF.Abs(value.DirectionY)>1) throw new ArgumentException("Invalid ability intent.");
        var writer = CreateWriter(NetworkMessageType.AbilityCommand);
        writer.Put(value.Sequence); writer.Put(value.ObservedServerTick); writer.Put(value.AbilityId); WriteVector2(writer, value.Aim); writer.Put(value.DashDistance); WriteHeight(writer, value.AimHeight); WriteHeight(writer, value.DirectionY);writer.Put((byte)value.Intent);
        return writer;
    }

    public static bool TryReadAbilityCommand(NetDataReader reader, out AbilityCommand value)
    {
        value = default;
        if (reader.AvailableBytes != 31 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetUShort(out var id) || id == 0 || !TryReadVector2(reader, out var aim) ||
            !reader.TryGetFloat(out var distance) || !float.IsFinite(distance) || distance is < 0 or > 100 || !ReadHeight(reader, out var height) || !ReadHeight(reader, out var dy) || MathF.Abs(dy) > 1 || !reader.TryGetByte(out var intent) || !Enum.IsDefined((AbilityIntent)intent)) return false;
        value = new(sequence, tick, id, aim, distance, height, dy,(AbilityIntent)intent); return true;
    }

    public static NetDataWriter Write(AbilityResult value)
    {
        if (value.Sequence == 0 || !Enum.IsDefined(value.Outcome)) throw new ArgumentException("Invalid ability result.");
        var writer = CreateWriter(NetworkMessageType.AbilityResult);
        writer.Put(value.Sequence); writer.Put(value.ServerTick); writer.Put((byte)value.Outcome); return writer;
    }

    public static bool TryReadAbilityResult(NetDataReader reader, out AbilityResult value)
    {
        value = default;
        if (reader.AvailableBytes != 9 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var outcome) || !Enum.IsDefined((AbilityOutcome)outcome)) return false;
        value = new(sequence, tick, (AbilityOutcome)outcome); return true;
    }

    public static NetDataWriter Write(AbilityLoadout value)
    {
        if (!ValidLoadout(value)) throw new ArgumentException("Invalid ability loadout.");
        var writer = CreateWriter(NetworkMessageType.AbilityLoadout);
        writer.Put(value.EntityId.Value); writer.Put(value.ServerTick); writer.Put(value.Mana); writer.Put(value.MaxMana);
        writer.Put(value.LastInputSequence);writer.Put((byte)value.Abilities.Count);
        writer.Put(value.Segment);writer.Put(value.Segments);writer.Put(value.TotalProfiles);
        foreach (var slot in value.Abilities)
        {
            writer.Put(slot.Id); writer.Put((byte)slot.Form); writer.Put(slot.Range); writer.Put(slot.Radius); writer.Put(slot.Speed);
            writer.Put(slot.CastSeconds); writer.Put(slot.CooldownSeconds); writer.Put(slot.ManaCost); writer.Put(slot.ReadyInSeconds);
            writer.Put(slot.StaminaCost); writer.Put((byte)((byte)slot.Availability|(slot.HealthCost>0?128:0)|(slot.AllowFatalHealthCost?64:0)));
            WriteArea(writer, slot.Area); writer.Put(slot.RecoverySeconds);
            writer.Put(slot.Charges); writer.Put(slot.MaxCharges); writer.Put(slot.RechargeInSeconds); writer.Put(slot.RechargeDuration);
            writer.Put((byte)slot.ExecutionForm);writer.Put(slot.PendingEndCharges);
            if(slot.HealthCost>0)writer.Put(slot.HealthCost);
            if(slot.Form==AbilityForm.Projectile)writer.Put(slot.ProjectileLaunchHeight);
            if(slot.Form==AbilityForm.Melee){writer.Put(slot.MeleeOriginHeight);writer.Put(slot.MeleeThickness);}
            if(slot.Form==AbilityForm.GroundArea){writer.Put(slot.GroundVolumeHeight);writer.Put(slot.GroundRequiresSurface);}
            if(slot.Form==AbilityForm.Dash){writer.Put(slot.DodgeDurationSeconds);writer.Put(slot.DodgeProtectionSeconds);writer.Put((byte)slot.DodgeProtectedThreats);}
            if(slot.ExecutionForm is CombatActionForm.Channel or CombatActionForm.Hold or CombatActionForm.Toggle)
            {writer.Put(slot.ChannelManaPerSecond);writer.Put(slot.ChannelStaminaPerSecond);writer.Put(slot.ChannelMaximumSeconds);}
            if(slot.ExecutionForm==CombatActionForm.Hold)writer.Put(slot.SustainCanTurn);
            if(slot.ExecutionForm==CombatActionForm.Toggle)
                writer.Put((byte)((byte)slot.ToggleState | (slot.ToggleOccupiesAction?4:0) | (byte)slot.ToggleStopOn<<3));
            if(slot.ExecutionForm==CombatActionForm.Charged)
            {writer.Put(slot.ChargeSeconds);writer.Put(slot.ChargeAutoRelease);writer.Put(slot.ChargeCanTurn);}
        }
        if(writer.Length>NetworkConstants.MaxGamePacketBytes-RegionEnvelopeBytes)throw new ArgumentException("Ability loadout exceeds packet budget.");
        return writer;
    }

    public static bool TryReadAbilityLoadout(NetDataReader reader, out AbilityLoadout value)
    {
        value = default;
        if (reader.AvailableBytes < 36 || reader.AvailableBytes>NetworkConstants.MaxGamePacketBytes-RegionEnvelopeBytes-2 || !reader.TryGetULong(out var actor) || actor == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetDouble(out var mana) || !reader.TryGetDouble(out var maximum) ||
            !reader.TryGetUInt(out var lastInput) || !reader.TryGetByte(out var count) || count > NetworkConstants.MaxAbilityProfiles ||
            !reader.TryGetByte(out var segment) || !reader.TryGetByte(out var segments) || !reader.TryGetByte(out var totalProfiles) || reader.AvailableBytes < count * 98) return false;
        var slots = new AbilityProfile[count];
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryGetUShort(out var id) || !reader.TryGetByte(out var form) || !reader.TryGetFloat(out var range) ||
                !reader.TryGetFloat(out var radius) || !reader.TryGetFloat(out var speed) || !reader.TryGetDouble(out var cast) ||
                !reader.TryGetDouble(out var cooldown) || !reader.TryGetDouble(out var cost) || !reader.TryGetDouble(out var ready) ||
                !reader.TryGetDouble(out var stamina) || !reader.TryGetByte(out var available) || !ReadArea(reader, out var area) || !reader.TryGetDouble(out var recovery) ||
                !reader.TryGetByte(out var charges) || !reader.TryGetByte(out var maximumCharges) || !reader.TryGetDouble(out var recharge) || !reader.TryGetDouble(out var rechargeDuration) ||
                !reader.TryGetByte(out var execution) || !Enum.IsDefined((CombatActionForm)execution) || !reader.TryGetByte(out var pendingEnd)) return false;
            var healthCost=0d;var fatalHealth=(available&64)!=0;
            if((available&128)!=0&&(!reader.TryGetDouble(out healthCost)||healthCost<=0))return false;
            double manaRate=0,staminaRate=0,channelMaximum=0,chargeSeconds=0;byte autoRelease=0,canTurn=0,sustainTurn=0;
            byte toggleState=0,toggleOccupies=0,toggleStops=0;
            var launchHeight=0f;var meleeOrigin=0f;var meleeThickness=0f;var groundHeight=0f;byte connected=0;
            double dodgeDuration=0,dodgeProtection=0;byte dodgeThreats=0;
            if((AbilityForm)form==AbilityForm.Projectile && !reader.TryGetFloat(out launchHeight))return false;
            if((AbilityForm)form==AbilityForm.Melee && (!reader.TryGetFloat(out meleeOrigin) || !reader.TryGetFloat(out meleeThickness)))return false;
            if((AbilityForm)form==AbilityForm.GroundArea && (!reader.TryGetFloat(out groundHeight) || !reader.TryGetByte(out connected) || connected>1))return false;
            if((AbilityForm)form==AbilityForm.Dash && (!reader.TryGetDouble(out dodgeDuration) || !reader.TryGetDouble(out dodgeProtection) || !reader.TryGetByte(out dodgeThreats)))return false;
            if((CombatActionForm)execution is CombatActionForm.Channel or CombatActionForm.Hold or CombatActionForm.Toggle &&
                (!reader.TryGetDouble(out manaRate) || !reader.TryGetDouble(out staminaRate) || !reader.TryGetDouble(out channelMaximum)))return false;
            if((CombatActionForm)execution==CombatActionForm.Hold && (!reader.TryGetByte(out sustainTurn) || sustainTurn>1))return false;
            if((CombatActionForm)execution==CombatActionForm.Toggle)
            {
                if(!reader.TryGetByte(out var flags))return false;
                toggleState=(byte)(flags&3);toggleOccupies=(byte)((flags>>2)&1);toggleStops=(byte)(flags>>3);
            }
            if((CombatActionForm)execution==CombatActionForm.Charged &&
                (!reader.TryGetDouble(out chargeSeconds) || !reader.TryGetByte(out autoRelease) || autoRelease>1 || !reader.TryGetByte(out canTurn) || canTurn>1))return false;
            slots[i] = new(id, (AbilityForm)form, range, radius, speed, cast, cooldown, cost, ready, stamina, (AbilityAvailability)(available&63), area, recovery,charges,maximumCharges,recharge,rechargeDuration,
                (CombatActionForm)execution,manaRate,staminaRate,channelMaximum,pendingEnd,chargeSeconds,autoRelease==1,canTurn==1,sustainTurn==1,
                (ToggleState)toggleState,toggleOccupies==1,(ToggleStopTriggers)toggleStops,launchHeight,meleeOrigin,meleeThickness,groundHeight,connected==1,
                dodgeDuration,dodgeProtection,(DodgeThreats)dodgeThreats,healthCost,fatalHealth);
        }
        var candidate = new AbilityLoadout(new(actor), tick, mana, maximum, slots,lastInput,segment,segments,totalProfiles);
        if (reader.AvailableBytes!=0 || !ValidLoadout(candidate)) return false;
        value = candidate; return true;
    }

    public static NetDataWriter Write(AbilityEffectState value)
    {
        if (!ValidEffect(value)) throw new ArgumentException("Invalid ability effect.");
        var writer = CreateWriter(NetworkMessageType.AbilityEffectState);
        writer.Put(value.EffectId); writer.Put(value.ActorId.Value); writer.Put(value.Sequence); writer.Put(value.ServerTick);
        writer.Put(value.AbilityId); writer.Put((byte)value.Form); writer.Put((byte)value.Phase);
        WriteVector2(writer, value.Origin); WriteVector2(writer, value.Position); WriteVector2(writer, value.Direction);
        writer.Put(value.Radius); writer.Put(value.Speed); writer.Put(value.RemainingSeconds); WriteHeight(writer, value.OriginHeight); WriteHeight(writer, value.Height); WriteHeight(writer, value.DirectionY); WriteArea(writer, value.Area);
        if(value.Form==AbilityForm.Melee)writer.Put(value.MeleeThickness);
        if(value.Form==AbilityForm.GroundArea)writer.Put(value.GroundVolumeHeight);return writer;
    }

    public static bool TryReadAbilityEffectState(NetDataReader reader, out AbilityEffectState value)
    {
        value = default;
        if (reader.AvailableBytes!=76+AbilityArea.WireBytes && reader.AvailableBytes!=80+AbilityArea.WireBytes || !reader.TryGetULong(out var effect) || !reader.TryGetULong(out var actor) ||
            !reader.TryGetUInt(out var sequence) || !reader.TryGetUInt(out var tick) || !reader.TryGetUShort(out var id) ||
            !reader.TryGetByte(out var form) || !reader.TryGetByte(out var phase) || !TryReadVector2(reader, out var origin) ||
            !TryReadVector2(reader, out var position) || !TryReadVector2(reader, out var direction) ||
            !reader.TryGetFloat(out var radius) || !reader.TryGetFloat(out var speed) || !reader.TryGetFloat(out var remaining) || !ReadHeight(reader, out var originHeight) || !ReadHeight(reader, out var height) || !ReadHeight(reader, out var dy) || MathF.Abs(dy) > 1 || !ReadArea(reader, out var area)) return false;
        var thickness=0f;var groundHeight=0f;
        if((AbilityForm)form==AbilityForm.Melee && !reader.TryGetFloat(out thickness))return false;
        if((AbilityForm)form==AbilityForm.GroundArea && !reader.TryGetFloat(out groundHeight))return false;
        var candidate = new AbilityEffectState(effect, new(actor), sequence, tick, id, (AbilityForm)form, (AbilityPhase)phase,
            origin, position, direction, radius, speed, remaining, originHeight, height, dy, area,thickness,groundHeight);
        if (reader.AvailableBytes!=0 || !ValidEffect(candidate)) return false;
        value = candidate; return true;
    }

    public static NetDataWriter Write(AbilityHit value)
    {
        if (!ValidHit(value)) throw new ArgumentException("Invalid ability hit.");
        var writer = CreateWriter(NetworkMessageType.AbilityHit);
        writer.Put(value.EffectId); writer.Put(value.ActorId.Value); writer.Put(value.TargetId.Value); writer.Put(value.ServerTick);
        writer.Put(value.Damage); writer.Put(value.TargetHealth); writer.Put((byte)value.Guard);
        writer.Put(value.Critical); writer.Put(value.Evaded);writer.Put((byte)value.Tags); return writer;
    }

    public static bool TryReadAbilityHit(NetDataReader reader, out AbilityHit value)
    {
        value = default;
        if (reader.AvailableBytes != 48 || !reader.TryGetULong(out var effect) || !reader.TryGetULong(out var actor) ||
            !reader.TryGetULong(out var target) || !reader.TryGetUInt(out var tick) || !reader.TryGetDouble(out var damage) ||
            !reader.TryGetDouble(out var health) || !reader.TryGetByte(out var guard) ||
            !reader.TryGetByte(out var critical) || critical > 1 || !reader.TryGetByte(out var evaded) || evaded > 1 || !reader.TryGetByte(out var tags)) return false;
        var candidate = new AbilityHit(effect, new(actor), new(target), tick, damage, health, (GuardImpact)guard, critical == 1, evaded == 1,(CombatHitTags)tags);
        if (!ValidHit(candidate)) return false;
        value = candidate; return true;
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool NonNegative(double value) => double.IsFinite(value) && value >= 0;
    private static void WriteArea(NetDataWriter writer, AbilityArea area)
    {
        writer.Put((byte)area.Shape); writer.Put(area.Length); writer.Put(area.HalfWidth);
        writer.Put(area.HalfAngleRadians); writer.Put((byte)(area.Stationary ? 1 : 0));
    }
    private static bool ReadArea(NetDataReader reader, out AbilityArea area)
    {
        area = default;
        if (reader.AvailableBytes < AbilityArea.WireBytes || !reader.TryGetByte(out var shape) ||
            !reader.TryGetFloat(out var length) || !reader.TryGetFloat(out var width) ||
            !reader.TryGetFloat(out var angle) || !reader.TryGetByte(out var stationary) || stationary > 1) return false;
        var candidate = new AbilityArea((AbilityAreaShape)shape, length, width, angle, stationary == 1);
        if (!candidate.IsValid) return false;
        area = candidate; return true;
    }
    private static bool ValidProfile(AbilityProfile value) => value.Id != 0 && Enum.IsDefined(value.Form) &&
        float.IsFinite(value.ProjectileLaunchHeight) && value.ProjectileLaunchHeight is >= 0 and <= 128 &&
        (value.Form==AbilityForm.Projectile || value.ProjectileLaunchHeight==0) &&
        float.IsFinite(value.MeleeOriginHeight) && value.MeleeOriginHeight is >=0 and <=128 &&
        float.IsFinite(value.MeleeThickness) && value.MeleeThickness is >=0 and <=128 && value.MeleeThickness*.5<=value.Range &&
        (value.Form==AbilityForm.Melee || value.MeleeOriginHeight==0 && value.MeleeThickness==0) &&
        float.IsFinite(value.GroundVolumeHeight) && value.GroundVolumeHeight is >=0 and <=128 &&
        (value.Form==AbilityForm.GroundArea || value.GroundVolumeHeight==0 && !value.GroundRequiresSurface) &&
        NonNegative(value.DodgeDurationSeconds) && value.DodgeDurationSeconds<=60 &&
        NonNegative(value.DodgeProtectionSeconds) && value.DodgeProtectionSeconds<=value.DodgeDurationSeconds &&
        (value.DodgeProtectedThreats&~DodgeThreats.All)==0 &&
        ((value.DodgeProtectionSeconds>0)==(value.DodgeProtectedThreats!=DodgeThreats.None)) &&
        (value.Form==AbilityForm.Dash ? value.DodgeDurationSeconds>0 && value.ExecutionForm==CombatActionForm.Instant :
            value.DodgeDurationSeconds==0 && value.DodgeProtectionSeconds==0 && value.DodgeProtectedThreats==DodgeThreats.None) &&
        float.IsFinite(value.Range) && value.Range > 0 && float.IsFinite(value.Radius) && value.Radius > 0 &&
        float.IsFinite(value.Speed) && value.Speed >= 0 && (value.Form is not (AbilityForm.Projectile or AbilityForm.Dash) || value.Speed > 0) &&
        NonNegative(value.CastSeconds) && (value.Form != AbilityForm.Dash || value.CastSeconds == 0) &&
        NonNegative(value.CooldownSeconds) && value.MaxCharges>0 && value.Charges+value.PendingEndCharges<=value.MaxCharges &&
        NonNegative(value.RechargeInSeconds) && NonNegative(value.RechargeDuration) && value.RechargeInSeconds<=value.RechargeDuration &&
        (value.Charges+value.PendingEndCharges==value.MaxCharges ? value.RechargeInSeconds==0 && value.RechargeDuration==0 : value.RechargeDuration>0) &&
        (value.Charges>0 ? value.ReadyInSeconds==0 : value.ReadyInSeconds==value.RechargeInSeconds) &&
        NonNegative(value.ManaCost) && NonNegative(value.HealthCost) && (!value.AllowFatalHealthCost || value.HealthCost>0) &&
        NonNegative(value.ReadyInSeconds) && NonNegative(value.StaminaCost) && Enum.IsDefined(value.Availability) && value.Area.IsValid && NonNegative(value.RecoverySeconds) &&
        Enum.IsDefined(value.ExecutionForm) && NonNegative(value.ChannelManaPerSecond) && NonNegative(value.ChannelStaminaPerSecond) && NonNegative(value.ChannelMaximumSeconds) &&
        (value.ExecutionForm is CombatActionForm.Channel or CombatActionForm.Hold or CombatActionForm.Toggle || value.PendingEndCharges==0 && value.ChannelManaPerSecond==0 && value.ChannelStaminaPerSecond==0 && value.ChannelMaximumSeconds==0) &&
        (value.ExecutionForm==CombatActionForm.Hold || !value.SustainCanTurn) &&
        Enum.IsDefined(value.ToggleState) && ((value.ToggleStopOn&~(ToggleStopTriggers.Movement|ToggleStopTriggers.Skill|ToggleStopTriggers.BasicAttack|ToggleStopTriggers.Defense|ToggleStopTriggers.EnemyInterrupt))==0) &&
        (value.ExecutionForm==CombatActionForm.Toggle || value.ToggleState==ToggleState.Off && !value.ToggleOccupiesAction && value.ToggleStopOn==ToggleStopTriggers.None) &&
        NonNegative(value.ChargeSeconds) && (value.ExecutionForm==CombatActionForm.Charged ? value.ChargeSeconds>0 && value.Form!=AbilityForm.Dash : value.ChargeSeconds==0 && !value.ChargeAutoRelease && !value.ChargeCanTurn);

    private static bool ValidLoadout(AbilityLoadout value)
    {
        if (!value.EntityId.IsValid || !NonNegative(value.Mana) || !NonNegative(value.MaxMana) || value.Mana > value.MaxMana ||
            value.Abilities is null || value.Abilities.Count > NetworkConstants.MaxAbilityProfiles) return false;
        if(value.Segments is 0 or >NetworkConstants.MaxAbilityProfiles||value.Segment>=value.Segments||
            (value.Segments==1?value.Segment!=0||value.TotalProfiles!=0:
                value.TotalProfiles>NetworkConstants.MaxAbilityProfiles||value.TotalProfiles<value.Segments||
                value.Abilities.Count==0||value.Abilities.Count>=value.TotalProfiles))return false;
        for (var i = 0; i < value.Abilities.Count; i++)
        {
            if (!ValidProfile(value.Abilities[i])) return false;
            for (var j = 0; j < i; j++) if (value.Abilities[j].Id == value.Abilities[i].Id) return false;
        }
        return true;
    }

    private static bool ValidEffect(AbilityEffectState value) => value.EffectId != 0 && value.ActorId.IsValid &&
        value.Sequence != 0 && (value.AbilityId != 0 || value.Form==AbilityForm.Projectile && value.Phase is AbilityPhase.Flying or AbilityPhase.Finished) && Enum.IsDefined(value.Form) && Enum.IsDefined(value.Phase) &&
        Finite(value.Origin) && Finite(value.Position) &&
        float.IsFinite(value.DirectionY) && MathF.Abs(value.DirectionY)<=1 &&
        (value.Form is AbilityForm.Projectile or AbilityForm.Melee
            ? value.Direction.LengthSquared()+value.DirectionY*value.DirectionY is >= .99f and <= 1.01f
            : value.DirectionY==0 && BasicAttackShape.IsValidDirection(value.Direction)) &&
        float.IsFinite(value.Radius) && value.Radius > 0 && float.IsFinite(value.Speed) && value.Speed >= 0 &&
        (value.Form is not (AbilityForm.Projectile or AbilityForm.Dash) || value.Speed > 0) &&
        (value.Phase == AbilityPhase.Finished || value.Phase==AbilityPhase.Charging && value.Form!=AbilityForm.Dash || value.Form switch
        {
            AbilityForm.Projectile => value.Phase is AbilityPhase.Telegraph or AbilityPhase.Flying,
            AbilityForm.GroundArea => value.Phase is AbilityPhase.Telegraph or AbilityPhase.Impact or AbilityPhase.Channeling or AbilityPhase.Recovery or AbilityPhase.Sustaining,
            AbilityForm.Recovery => value.Phase is AbilityPhase.Telegraph or AbilityPhase.Impact or AbilityPhase.Recovery or AbilityPhase.Channeling or AbilityPhase.Sustaining,
            AbilityForm.Melee => value.Phase is AbilityPhase.Telegraph or AbilityPhase.Impact or AbilityPhase.Recovery,
            AbilityForm.Dash => value.Phase == AbilityPhase.Dash,
            _ => false
        }) && float.IsFinite(value.RemainingSeconds) && value.RemainingSeconds >= 0 && value.Area.IsValid &&
        float.IsFinite(value.MeleeThickness) && value.MeleeThickness is >=0 and <=128 &&
        (value.Form==AbilityForm.Melee ? value.MeleeThickness*.5<=value.Radius : value.MeleeThickness==0) &&
        float.IsFinite(value.GroundVolumeHeight) && value.GroundVolumeHeight is >=0 and <=128 &&
        (value.Form==AbilityForm.GroundArea || value.GroundVolumeHeight==0);

    private static bool ValidHit(AbilityHit value) => value.EffectId != 0 && value.ActorId.IsValid && value.TargetId.IsValid &&
        NonNegative(value.Damage) && NonNegative(value.TargetHealth) && Enum.IsDefined(value.Guard) && CombatHitTagRules.IsValid(value.Tags) &&
        (!value.Evaded || value.Damage == 0 && !value.Critical && value.Guard == GuardImpact.None) &&
        (value.Guard!=GuardImpact.Dodged || value.Damage==0 && !value.Critical && !value.Evaded);
}
