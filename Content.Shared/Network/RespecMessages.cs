namespace Content.Shared.Network;

public enum RespecAction:byte { Quote,Confirm }
public enum RespecOutcome:byte { Accepted,AlreadyProcessed,Unavailable,InvalidState,InCombat,Busy,TooFar,NothingToReset,
    MigrationRequired,StaleQuote,InsufficientCoins,RateLimited }
public readonly record struct RespecCommand(uint Sequence,RespecAction Action,NetworkEntityId NpcId,ulong QuoteId=0,ulong Operation=0);
/// <summary>Owner-only quote. Primary stat order and derived stat order match ProgressionState/StatPreview.</summary>
public readonly record struct RespecQuote(uint Sequence,uint ServerTick,NetworkEntityId NpcId,ulong QuoteId,ulong Operation,
    int Price,int Coins,int ReturnedPoints,int AvailableAfter,float ValidSeconds,bool FirstFree,
    double[] PrimaryBefore,double[] PrimaryAfter,double[] Current,double[] Projected);
public readonly record struct RespecResult(uint Sequence,uint ServerTick,ulong Operation,RespecOutcome Outcome,int PaidCoins,int ReturnedPoints);
