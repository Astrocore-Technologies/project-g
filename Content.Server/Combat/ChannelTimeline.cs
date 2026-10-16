using Content.Server.Data;
using Content.Server.Stats;

namespace Content.Server.Combat;

internal readonly record struct ChannelAdvance(double PaidSeconds,double Mana,double Stamina,int Pulses,bool Finished,bool Starved,bool ManaLimited=false);

/// <summary>Paid continuous time and a separate pulse cadence. No server-step or integer resource rounding.</summary>
internal sealed class ChannelTimeline
{
    private readonly ChannelDefinition _definition;
    private readonly double _startedAt;
    private double _paidUntil;
    private ulong _nextPulse;
    internal ChannelTimeline(ChannelDefinition definition,double now)
    {
        definition.Validate("channel execution");
        if(!double.IsFinite(now) || now<0 || now+definition.IntervalSeconds<=now ||
            definition.MaximumSeconds is {} maximum && (!double.IsFinite(now+maximum) || now+maximum<=now))
            throw new ArgumentException("Channel timing cannot be represented on the simulation clock.");
        _definition=definition;_startedAt=_paidUntil=now;_nextPulse=definition.PulseAtStart?0UL:1UL;
    }
    internal bool Finished {get;private set;}
    internal double PaidUntil=>_paidUntil;
    internal double NextEventAt=>Math.Min(_startedAt+_nextPulse*_definition.IntervalSeconds,
        _definition.MaximumSeconds is {} maximum?_startedAt+maximum:double.PositiveInfinity);
    internal double? Remaining=>_definition.MaximumSeconds is {} maximum?Math.Max(0,_startedAt+maximum-_paidUntil):null;

    internal ChannelAdvance Advance(double now,double mana,double stamina)
    {
        if(!double.IsFinite(now) || now<_paidUntil || !double.IsFinite(mana) || mana<0 || !double.IsFinite(stamina) || stamina<0)
            throw new ArgumentException("Invalid channel clock or available resources.");
        if(Finished)return new(0,0,0,0,true,false);
        var desired=Math.Min(now-_paidUntil,Remaining ?? double.MaxValue);
        var manaLimit=_definition.ManaPerSecond>0?mana/_definition.ManaPerSecond:double.PositiveInfinity;
        var staminaLimit=_definition.StaminaPerSecond>0?stamina/_definition.StaminaPerSecond:double.PositiveInfinity;
        var funded=Math.Min(desired,Math.Min(manaLimit,staminaLimit));
        var through=_paidUntil+funded;
        // Work is computed before committing elapsed time; absurd unrepresentable content fails without debiting resources.
        var pulseAt=_startedAt+_nextPulse*_definition.IntervalSeconds;
        var count=pulseAt<=Math.BitIncrement(Math.BitIncrement(through))
            ? Math.Floor((Math.BitIncrement(Math.BitIncrement(through))-pulseAt)/_definition.IntervalSeconds)+1 : 0;
        if(!double.IsFinite(count) || count>int.MaxValue || count>ulong.MaxValue-_nextPulse)
            throw new InvalidOperationException("Channel cadence exceeds representable work for this simulation step.");
        _nextPulse+=(ulong)count;_paidUntil=through;
        var starved=funded<desired;
        Finished=starved || Remaining is <=0;
        return new(funded,funded==manaLimit?mana:Math.Min(mana,StatMath.Multiply(funded,_definition.ManaPerSecond)),
            funded==staminaLimit?stamina:Math.Min(stamina,StatMath.Multiply(funded,_definition.StaminaPerSecond)),
            (int)count,Finished,starved,starved && manaLimit<=staminaLimit);
    }
}
