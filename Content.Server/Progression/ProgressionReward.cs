namespace Content.Server.Progression;

public enum ExperienceActivity : byte { PveVictory,MapExploration,Discovery,CraftCompleted,QuestCompleted,TradeCompleted,DeliveryCompleted,LiveDmObjectiveCompleted }

/// <summary>Created only after the producer has claimed its durable operation/visit/life receipt.</summary>
internal readonly record struct ProgressionReward(ExperienceActivity Activity,string SourceId,int Revision,int Amount)
{
    internal void Validate()
    {
        if(!Enum.IsDefined(Activity) || SourceId is not {Length:>0 and <=192} || Revision<1 || Amount<=0)
            throw new InvalidOperationException("Invalid confirmed EXP reward.");
    }
}
