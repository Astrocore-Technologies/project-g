using Content.Shared.Combat;

namespace Content.Server.Combat;

internal readonly record struct ControlExposure(double Duration,double Strength,double BudgetCost)
{
    internal bool Accepted=>Duration>0 && Strength>0;
}

internal readonly record struct AcrSnapshot(ControlCategory Category,double Used,double ResetInSeconds);

/// <summary>One target owns five independent budgets. Actual effect lifetime is supplied by the control runner.</summary>
internal sealed class AdaptiveControlLedger
{
    private struct BudgetState { internal double Used, ResetAt; }
    private readonly BudgetState[] _states=new BudgetState[5];
    private readonly ControlBalance _balance;

    internal AdaptiveControlLedger(ControlBalance balance) { balance.Validate(); _balance=balance; }

    internal static ControlCategory Category(CrowdControlKind kind)=>kind switch
    {
        CrowdControlKind.Stun or CrowdControlKind.Freeze or CrowdControlKind.Sleep or CrowdControlKind.Fear or
            CrowdControlKind.Knockdown or CrowdControlKind.Knockup=>ControlCategory.Hard,
        CrowdControlKind.Root or CrowdControlKind.Knockback or CrowdControlKind.Pull=>ControlCategory.Movement,
        CrowdControlKind.Silence or CrowdControlKind.Disarm=>ControlCategory.Action,
        CrowdControlKind.Slow or CrowdControlKind.Blind=>ControlCategory.Soft,
        CrowdControlKind.Taunt=>ControlCategory.Targeting,
        _=>throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal AcrSnapshot Snapshot(ControlCategory category,double now)
    {
        Refresh(category,now); var state=_states[(int)category];
        return new(category,state.Used,Math.Max(0,state.ResetAt-now));
    }

    // Quotes do not reserve or mutate budget. The runner commits only after immunity, geometry and state validation.
    internal ControlExposure QuoteDuration(ControlCategory category,double duration,double now,double existingRemaining=0)
    {
        ValidateRequest(duration,1,now,existingRemaining);
        var state=Snapshot(category,now); var budget=_balance.Budget(category);
        var desired=duration/(1+_balance.Lambda*(state.Used/budget));
        var added=Math.Min(Math.Max(0,desired-existingRemaining),Math.Max(0,budget-state.Used));
        return added<=0 ? default : new(existingRemaining+added,1,added);
    }

    internal ControlExposure QuoteStrength(ControlCategory category,double duration,double strength,double normalization,double now)
    {
        ValidateRequest(duration,strength,now,0);
        if(!double.IsFinite(normalization) || normalization<=0) throw new ArgumentException("Invalid control strength normalization.");
        var state=Snapshot(category,now); var budget=_balance.Budget(category);
        var normalized=strength/normalization/(1+_balance.Lambda*(state.Used/budget));
        normalized=Math.Min(normalized,Math.Max(0,budget-state.Used)/duration);
        return normalized<=0 ? default : new(duration,normalized*normalization,normalized*duration);
    }

    // Refresh preserves an existing strength; charge only the extension, with DR applied to the incoming duration.
    internal ControlExposure QuoteRefresh(ControlCategory category,double duration,double strength,double normalization,
        double existingRemaining,double now)
    {
        ValidateRequest(duration,strength,now,existingRemaining);
        if(!double.IsFinite(normalization) || normalization<=0) throw new ArgumentException("Invalid control strength normalization.");
        var state=Snapshot(category,now); var budget=_balance.Budget(category);
        var desired=duration/(1+_balance.Lambda*(state.Used/budget));
        var normalized=strength/normalization;
        var added=Math.Min(Math.Max(0,desired-existingRemaining),Math.Max(0,budget-state.Used)/normalized);
        return added<=0 ? default : new(existingRemaining+added,strength,normalized*added);
    }

    internal void Commit(ControlCategory category,ControlExposure exposure,double lastEffectEndsAt,double now)
    {
        var state=Snapshot(category,now); var budget=_balance.Budget(category);
        if(!exposure.Accepted || !double.IsFinite(exposure.BudgetCost) || exposure.BudgetCost<=0 ||
            exposure.BudgetCost>budget-state.Used+1e-10 || !double.IsFinite(lastEffectEndsAt) || lastEffectEndsAt<=now)
            throw new ArgumentException("Invalid or stale ACR exposure commit.");
        ref var target=ref _states[(int)category];
        target.Used=Math.Min(budget,state.Used+exposure.BudgetCost);
        target.ResetAt=lastEffectEndsAt+_balance.ResetSeconds;
    }

    internal void EffectsRemoved(ControlCategory category,double lastRemainingEndAt,double now)
    {
        Refresh(category,now);
        ref var state=ref _states[(int)category];
        if(state.Used>0) state.ResetAt=Math.Max(now,lastRemainingEndAt)+_balance.ResetSeconds;
    }

    internal void Restore(AcrSnapshot snapshot,double now,double offlineSeconds)
    {
        var budget=_balance.Budget(snapshot.Category);
        if(!double.IsFinite(now) || now<0 || !double.IsFinite(offlineSeconds) || offlineSeconds<0 ||
            !double.IsFinite(snapshot.Used) || snapshot.Used<0 || snapshot.Used>budget ||
            !double.IsFinite(snapshot.ResetInSeconds) || snapshot.ResetInSeconds<0)
            throw new ArgumentException("Invalid saved ACR state.");
        var remaining=Math.Max(0,snapshot.ResetInSeconds-offlineSeconds);
        _states[(int)snapshot.Category]=remaining==0 ? default : new(){Used=snapshot.Used,ResetAt=now+remaining};
    }

    private void Refresh(ControlCategory category,double now)
    {
        if(!Enum.IsDefined(category) || !double.IsFinite(now) || now<0) throw new ArgumentException("Invalid ACR category or clock.");
        if(now>=_states[(int)category].ResetAt) _states[(int)category]=default;
    }

    private static void ValidateRequest(double duration,double strength,double now,double existingRemaining)
    {
        if(!double.IsFinite(duration) || duration<=0 || !double.IsFinite(strength) || strength<=0 ||
            !double.IsFinite(now) || now<0 || !double.IsFinite(existingRemaining) || existingRemaining<0)
            throw new ArgumentException("Invalid control duration, strength or clock.");
    }
}
