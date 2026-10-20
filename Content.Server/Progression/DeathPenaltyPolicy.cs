namespace Content.Server.Progression;

/// <summary>GDD 03: five percent of the level threshold, rounded down, without level loss.</summary>
public static class DeathPenaltyPolicy
{
    public static int PveLoss(ExperienceCurve curve,int level,int experience)
    {
        var threshold=curve.NextLevel(level);
        if(experience<0 || (threshold==0?experience!=0:experience>=threshold))
            throw new ArgumentException("Invalid experience at death.");
        return Math.Min(experience,threshold/20);
    }
}
