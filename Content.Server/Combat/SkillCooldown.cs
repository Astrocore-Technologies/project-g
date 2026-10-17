namespace Content.Server.Combat;

internal readonly record struct SkillCooldownState(byte Available,byte Maximum,double RechargeIn,double RechargeDuration,byte PendingEnd=0)
{
    internal double ReadyIn=>Available>0 ? 0 : RechargeIn;
}

/// <summary>Sequential charges. A running interval keeps its duration; later intervals use the latest configuration.</summary>
internal sealed class SkillCooldown
{
    private byte _available,_pendingEnd;
    private double _nextAt,_runningDuration;
    internal SkillCooldown(byte maximum,double cooldown)
    {
        if(maximum==0) throw new ArgumentException("A skill needs at least one charge.");
        ValidateDuration(cooldown); Maximum=maximum; _available=maximum; ConfiguredDuration=cooldown;
    }

    internal byte Maximum { get; }
    internal double ConfiguredDuration { get; private set; }
    internal bool Recovering=>_available+_pendingEnd<Maximum;
    internal double NextRechargeAt=>_nextAt;

    internal bool Advance(double now)
    {
        ValidateClock(now); var before=_available;
        // At most Maximum (a bounded content byte) transitions, including zero-duration cooldowns.
        while(Recovering && now>=_nextAt)
        {
            _available++;
            if(Recovering) { _runningDuration=ConfiguredDuration; _nextAt=Content.Server.Stats.StatMath.Add(_nextAt,ConfiguredDuration); }
            else _nextAt=_runningDuration=0;
        }
        return _available!=before;
    }

    internal SkillCooldownState State(double now)
    {
        Advance(now); return new(_available,Maximum,Recovering ? Math.Max(0,_nextAt-now) : 0,_runningDuration,_pendingEnd);
    }

    internal void Configure(double cooldown,double now)
    {
        ValidateDuration(cooldown); Advance(now); ConfiguredDuration=cooldown;
    }

    internal bool Consume(double now)
    {
        Advance(now); if(_available==0) return false;
        var start=!Recovering; _available--;
        if(start) { _runningDuration=ConfiguredDuration; _nextAt=Content.Server.Stats.StatMath.Add(now,ConfiguredDuration); }
        Advance(now); return true;
    }

    internal bool ReserveUntilEnd(double now)
    {
        Advance(now);if(_available==0)return false;
        _available--;_pendingEnd++;return true;
    }

    internal bool FinishReservation(double now)
    {
        Advance(now);if(_pendingEnd==0)return false;
        var start=!Recovering;_pendingEnd--;
        if(start){_runningDuration=ConfiguredDuration;_nextAt=Content.Server.Stats.StatMath.Add(now,ConfiguredDuration);}
        Advance(now);return true;
    }

    internal void Reset(double now)
    {
        ValidateClock(now); _available=Maximum; _pendingEnd=0;_nextAt=_runningDuration=0;
    }

    internal SkillCooldown Copy(double newCooldown,double now)
    {
        Advance(now);
        var copy=new SkillCooldown(Maximum,ConfiguredDuration) {_available=this._available,_pendingEnd=this._pendingEnd,_nextAt=this._nextAt,_runningDuration=this._runningDuration};
        copy.Configure(newCooldown,now); return copy;
    }

    internal void Restore(byte available,double remaining,double runningDuration,double now,double offlineSeconds,byte pendingEnd=0)
    {
        ValidateClock(now); ValidateDuration(remaining); ValidateDuration(runningDuration); ValidateDuration(offlineSeconds);
        if(available+pendingEnd>Maximum || available+pendingEnd==Maximum && remaining!=0 || available+pendingEnd<Maximum && remaining>runningDuration)
            throw new ArgumentException("Saved skill charges do not match their recovery interval.");
        _available=available;_pendingEnd=pendingEnd; _runningDuration=Recovering ? runningDuration : 0;
        _nextAt=Recovering ? now+remaining-offlineSeconds : 0;
        Advance(now);
        // A persisted channel is over. D058 starts its deferred interval now, without offline credit.
        while(_pendingEnd>0)FinishReservation(now);
    }

    private static void ValidateClock(double value)
    { if(!double.IsFinite(value) || value<0) throw new ArgumentException("Invalid cooldown clock."); }
    private static void ValidateDuration(double value)
    { if(!double.IsFinite(value) || value<0) throw new ArgumentException("Invalid cooldown duration."); }
}
