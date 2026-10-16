namespace Content.Server.Combat;

public sealed record CombatLifecycleBalance
{
    public double InCombatSeconds { get; init; } = 30;
    public double DisconnectBodySeconds { get; init; } = 60;
    public double CorpseSeconds { get; init; } = 60;
    public double RespawnSeconds {get;init;}=60;
    public double RespawnHealthFraction {get;init;}=.5;
    public double RespawnManaFraction {get;init;}=.5;
    public double RespawnStaminaFraction {get;init;}=1;
    internal void Validate()
    {
        if(!double.IsFinite(InCombatSeconds) || InCombatSeconds is <=0 or >86400 ||
            !double.IsFinite(DisconnectBodySeconds) || DisconnectBodySeconds is <=0 or >86400 ||
            !double.IsFinite(CorpseSeconds) || CorpseSeconds is <=0 or >86400 ||
            !double.IsFinite(RespawnSeconds) || RespawnSeconds is <0 or >86400 ||
            !Fraction(RespawnHealthFraction) || RespawnHealthFraction<=0 || !Fraction(RespawnManaFraction) || !Fraction(RespawnStaminaFraction))
            throw new ArgumentException("Invalid combat lifecycle balance.");
    }
    private static bool Fraction(double value)=>double.IsFinite(value)&&value is >=0 and <=1;
}
